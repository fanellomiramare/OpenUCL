namespace OpenUCL.Data;

/// <summary>Options for reading a classic (long) layout table: one observation per row.</summary>
public sealed class LongTableReadOptions
{
    /// <summary>CSV column separator. Default ';' (Italian/European). Ignored for .xlsx.</summary>
    public char ColumnSeparator { get; init; } = ';';

    public bool HasHeader { get; init; } = true;

    /// <summary>Name of the column that flags nondetect rows (e.g. "C" or "ND"). Null = no nondetects.</summary>
    public string? NdFlagColumn { get; init; }

    /// <summary>Value of the flag column marking a nondetect row. Default "ND" (case-insensitive).</summary>
    public string NdFlagValue { get; init; } = "ND";

    /// <summary>
    /// Column holding the detection limit for nondetect rows. When null, ProUCL convention
    /// applies: for a flagged row the value column itself carries the detection limit.
    /// </summary>
    public string? DetectionLimitColumn { get; init; }
}

/// <summary>
/// A column-oriented table read from a long-layout file. Cells hold double, string or null
/// (missing). One observation per row, one variable per column — the layout ProUCL uses.
/// </summary>
public sealed class LongTable
{
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<object?[]> Rows { get; }
    public int RowCount => Rows.Count;

    public LongTable(IReadOnlyList<string> columns, IReadOnlyList<object?[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public int ColumnIndex(string column)
    {
        for (int i = 0; i < Columns.Count; i++)
            if (string.Equals(Columns[i].Trim(), column.Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        throw new FormatException($"Column '{column}' not found. Available: {string.Join(", ", Columns)}");
    }

    public string GetString(int row, int col)
    {
        var cell = Rows[row][col];
        return cell switch
        {
            null => "",
            string s => s,
            double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            var o => o.ToString() ?? "",
        };
    }

    public double? GetDouble(int row, int col)
    {
        var cell = Rows[row][col];
        return cell switch
        {
            null => null,
            double d => d,
            string s => TryParseDouble(s.Trim(), out var v) ? v : null,
            _ => null,
        };
    }

    internal static bool TryParseDouble(string token, out double value)
    {
        if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;
        return double.TryParse(token.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
