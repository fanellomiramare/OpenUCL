using System.Globalization;

namespace OpenUCL.Data;

public sealed class PackedSeriesReadOptions
{
    /// <summary>Separator between columns of a line. Default ';' (Italian/European CSV).</summary>
    public char ColumnSeparator { get; init; } = ';';

    /// <summary>
    /// Separator between values packed inside the values cell.
    /// Null (default) = auto-detect per row among ';' '|' ',' tab.
    /// </summary>
    public char? ValueSeparator { get; init; }

    /// <summary>When true (default) the first non-empty line is treated as a header and ignored.</summary>
    public bool HasHeader { get; init; } = true;
}

/// <summary>
/// Streams series from a packed series file (one series per row:
/// point id, parameter, packed value list — see docs/format-packed.md).
/// Only one series is materialised at a time, so the file size is bounded by
/// disk space, not RAM.
/// </summary>
public static class PackedSeriesReader
{
    private static readonly char[] AutoDetectSeparators = { ';', '|', ',', '\t' };
    private static readonly char[] TrimChars = { '"', ' ' };

    public static IEnumerable<SeriesRecord> ReadFile(string path, PackedSeriesReadOptions? options = null)
    {
        var opts = options ?? new PackedSeriesReadOptions();
        using var reader = new StreamReader(path);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (opts.HasHeader && lineNumber == 1) continue;

            SeriesRecord record;
            try
            {
                var fields = SplitCsvLine(line, opts.ColumnSeparator);
                if (fields.Length < 3)
                    throw new FormatException($"expected at least 3 columns, found {fields.Length}");
                record = ParseRow(fields, opts);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                throw new FormatException(
                    $"{Path.GetFileName(path)}, line {lineNumber}: {ex.Message}", ex);
            }
            yield return record;
        }
    }

    public static SeriesRecord ParseRow(string[] fields, PackedSeriesReadOptions options)
    {
        string pointId = Unquote(fields[0]).Trim();
        string parameter = Unquote(fields[1]).Trim();
        if (pointId.Length == 0) throw new FormatException("empty point id (column 1)");
        if (parameter.Length == 0) throw new FormatException("empty parameter (column 2)");

        char valueSep = options.ValueSeparator ?? DetectValueSeparator(fields[2]);
        var (observations, skipped) = ParseValues(fields[2], valueSep);
        return new SeriesRecord(pointId, parameter, observations, skipped);
    }

    private static char DetectValueSeparator(string cell)
    {
        char best = ';';
        int bestCount = 0;
        foreach (char candidate in AutoDetectSeparators)
        {
            int count = 0;
            foreach (char c in cell) if (c == candidate) count++;
            if (count > bestCount) { best = candidate; bestCount = count; }
        }
        return best;
    }

    private static (List<ValueObservation> Observations, int Skipped) ParseValues(string cell, char separator)
    {
        var observations = new List<ValueObservation>();
        int skipped = 0;
        foreach (var raw in cell.Split(separator))
        {
            var token = raw.Trim().Trim('"').Trim();
            if (token.Length == 0 || IsMissingMarker(token)) { skipped++; continue; }

            if (token[0] == '<')
            {
                double dl = ParseDouble(token[1..]);
                observations.Add(ValueObservation.Nondetect(dl));
            }
            else
            {
                observations.Add(ValueObservation.Detect(ParseDouble(token)));
            }
        }
        return (observations, skipped);
    }

    private static bool IsMissingMarker(string token) =>
        token.Equals("NA", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("ND", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("NaN", StringComparison.OrdinalIgnoreCase);

    private static double ParseDouble(string token)
    {
        // Invariant '.' first; then accept ',' as decimal separator.
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return v;
        if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return v;
        throw new FormatException($"'{token}' is not a valid number");
    }

    private static string Unquote(string field) => field.Trim().Trim(TrimChars);

    /// <summary>Splits one CSV line honoring double-quote delimited fields ("a;b" counts as one field, "" escapes a quote).</summary>
    internal static string[] SplitCsvLine(string line, char separator)
    {
        var fields = new List<string>(8);
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == separator) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}
