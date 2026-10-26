namespace OpenUCL.Data;

/// <summary>Which columns of a long-layout table form the series, and by what to group.</summary>
public sealed class SeriesSelection
{
    /// <summary>Value columns (one series per column, like ProUCL "Select Variables").</summary>
    public required IReadOnlyList<string> ValueColumns { get; init; }

    /// <summary>Optional group column (like ProUCL "Select Group Column"): series are split per group.</summary>
    public string? GroupColumn { get; init; }
}

/// <summary>
/// Reads classic (long) layout files — one observation per row, one variable per column,
/// optional nondetect flag and detection limit columns, optional group column — from
/// CSV/TSV or .xlsx, and explodes them into per-(group × variable) series.
/// </summary>
public static class LongTableReader
{
    /// <summary>Reads the whole table into memory (long layout needs row access for grouping).</summary>
    public static LongTable ReadTable(string path, LongTableReadOptions? options = null)
    {
        var opts = options ?? new LongTableReadOptions();
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(path),
            ".xls" => ReadLegacyXls(path),
            _ => ReadCsv(path, opts),
        };
    }

    /// <summary>
    /// Explodes a long-layout table into series (one per group × value column), preserving
    /// file order. Nondetects are resolved via the flag/detection-limit columns.
    /// </summary>
    public static IEnumerable<SeriesRecord> ToSeries(LongTable table, SeriesSelection selection, LongTableReadOptions? options = null)
    {
        var opts = options ?? new LongTableReadOptions();

        int[] valueCols = selection.ValueColumns.Select(table.ColumnIndex).ToArray();
        int groupCol = selection.GroupColumn is null ? -1 : table.ColumnIndex(selection.GroupColumn);
        int flagCol = opts.NdFlagColumn is null ? -1 : table.ColumnIndex(opts.NdFlagColumn);
        int dlCol = opts.DetectionLimitColumn is null ? -1 : table.ColumnIndex(opts.DetectionLimitColumn);

        // groupKey -> per-value-column buffers
        var order = new List<string>();
        var groups = new Dictionary<string, List<ValueObservation>[]>();
        var missing = new Dictionary<string, int[]>();

        for (int row = 0; row < table.RowCount; row++)
        {
            string key = groupCol < 0 ? "" : table.GetString(row, groupCol).Trim();
            if (key.Length == 0 && groupCol >= 0) key = "(missing group)";

            if (!groups.TryGetValue(key, out var buffers))
            {
                buffers = new List<ValueObservation>[valueCols.Length];
                for (int i = 0; i < valueCols.Length; i++) buffers[i] = new List<ValueObservation>();
                groups[key] = buffers;
                order.Add(key);
                missing[key] = new int[valueCols.Length];
            }
            var miss = missing[key];

            for (int i = 0; i < valueCols.Length; i++)
            {
                double? value = table.GetDouble(row, valueCols[i]);
                if (value is null) { miss[i]++; continue; }

                bool isNd = flagCol >= 0 &&
                            string.Equals(table.GetString(row, flagCol).Trim(), opts.NdFlagValue,
                                StringComparison.OrdinalIgnoreCase);
                if (isNd)
                {
                    double dl = dlCol >= 0 ? table.GetDouble(row, dlCol) ?? value.Value : value.Value;
                    buffers[i].Add(ValueObservation.Nondetect(dl));
                }
                else
                {
                    buffers[i].Add(ValueObservation.Detect(value.Value));
                }
            }
        }

        foreach (var key in order)
        {
            for (int i = 0; i < valueCols.Length; i++)
            {
                yield return new SeriesRecord(
                    PointId: key,
                    Parameter: selection.ValueColumns[i].Trim(),
                    Observations: groups[key][i],
                    SkippedTokens: missing[key][i]);
            }
        }
    }

    private static LongTable ReadCsv(string path, LongTableReadOptions opts)
    {
        string[]? columns = null;
        var rows = new List<object?[]>();
        using var reader = new StreamReader(path);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = PackedSeriesReader.SplitCsvLine(line, opts.ColumnSeparator);

            if (columns is null)
            {
                columns = opts.HasHeader
                    ? fields
                    : fields.Select((_, i) => $"V{i + 1}").ToArray();
                if (opts.HasHeader) continue;
            }

            if (fields.Length > columns.Length)
                throw new FormatException($"{Path.GetFileName(path)}, line {lineNumber}: {fields.Length} fields but {columns.Length} columns were declared in the header.");
            var cells = new object?[columns.Length];
            for (int i = 0; i < Math.Min(fields.Length, columns.Length); i++)
            {
                var token = fields[i].Trim();
                if (token.Length == 0) continue;
                if (LongTable.TryParseDouble(token, out var d)) cells[i] = d;
                else cells[i] = token;
            }
            rows.Add(cells);
        }
        return new LongTable(columns ?? Array.Empty<string>(), rows);
    }

    private static LongTable ReadXlsx(string path)
    {
        var rowsDict = MiniExcelLibs.MiniExcel.Query(path, useHeaderRow: true)
            .Cast<IDictionary<string, object?>>()
            .ToList();
        var columns = new List<string>();
        if (rowsDict.Count > 0)
        {
            foreach (var key in rowsDict[0].Keys)
                columns.Add(key?.ToString() ?? "");
        }

        var rows = new List<object?[]>(rowsDict.Count);
        foreach (var dict in rowsDict)
        {
            var cells = new object?[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var cell = dict.TryGetValue(columns[i], out var v) ? NormalizeCell(v) : null;
                cells[i] = cell;
            }
            rows.Add(cells);
        }
        return new LongTable(columns, rows);
    }

    private static bool _encodingProviderRegistered;

    /// <summary>
    /// Reads legacy .xls (BIFF) files via ExcelDataReader: first worksheet, first row as header.
    /// Needed to open the sample datasets shipped with ProUCL.
    /// </summary>
    private static LongTable ReadLegacyXls(string path)
    {
        if (!_encodingProviderRegistered)
        {
            // ExcelDataReader needs codepage 1252 for legacy BIFF strings.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            _encodingProviderRegistered = true;
        }

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        if (!reader.Read()) return new LongTable(Array.Empty<string>(), Array.Empty<object?[]>());

        var columns = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetValue(i)?.ToString()?.Trim();
            columns.Add(string.IsNullOrWhiteSpace(name) ? $"V{i + 1}" : name!);
        }

        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var cells = new object?[columns.Count];
            bool any = false;
            for (int i = 0; i < columns.Count && i < reader.FieldCount; i++)
            {
                cells[i] = NormalizeCell(reader.GetValue(i));
                if (cells[i] is not null) any = true;
            }
            if (any) rows.Add(cells);
        }
        return new LongTable(columns, rows);
    }

    private static object? NormalizeCell(object? cell) => cell switch
    {
        null or double or string => cell,
        DateTime dt => dt.ToString("yyyy-MM-dd"),
        IConvertible => Convert.ToDouble(cell, System.Globalization.CultureInfo.InvariantCulture),
        _ => cell.ToString(),
    };
}
