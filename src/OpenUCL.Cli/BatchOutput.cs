using System.Globalization;
using System.Text;
using MiniExcelLibs;

namespace OpenUCL.Cli;

/// <summary>
/// Batch output (output-design.md, decisions #8-13/#72/#75): with `-o &lt;dir&gt;` the
/// sheet-producing commands write per-series sheets into &lt;dir&gt;/&lt;command&gt;/ and a
/// summary CSV plus XLSX (run-info header, one row per series) into &lt;dir&gt;. With
/// --bundle the sheets are appended into one combined file instead of one file
/// per series.
/// </summary>
internal sealed class BatchRun
{
    private readonly string _command;
    private readonly string _inputBase;
    private readonly bool _bundle;
    private readonly char _csvSep;
    private readonly HashSet<string> _usedSheetNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string[]> _rows = new();
    private string[]? _header;
    private bool _bundleStarted;

    public string Root { get; }
    public string SheetsDir { get; }
    public string SummaryPath { get; private set; } = "";
    public string SummaryXlsxPath { get; private set; } = "";

    public BatchRun(string root, string command, string inputPath, bool bundle, char csvSep = ',')
    {
        _command = command;
        _inputBase = Sanitize(Path.GetFileNameWithoutExtension(inputPath));
        _bundle = bundle;
        _csvSep = csvSep;
        Root = Path.GetFullPath(root);
        SheetsDir = Path.Combine(Root, command);
        Directory.CreateDirectory(SheetsDir);
    }

    /// <summary>Sets the summary column header (must be called before the first row).</summary>
    public void SetHeader(params string[] header)
    {
        _header = header.Length > 0 ? header : throw new ArgumentException("empty header");
    }

    /// <summary>Adds one summary row (padded to the header width).</summary>
    public void AddRow(params string[] cells)
    {
        if (_header is null) throw new InvalidOperationException("SetHeader before AddRow");
        var row = new string[_header.Length];
        for (int i = 0; i < _header.Length; i++) row[i] = i < cells.Length ? cells[i] : "";
        _rows.Add(row);
    }

    /// <summary>
    /// Writes one series sheet: a separate sanitized file, or an append to the
    /// combined bundle file. Returns the path written.
    /// </summary>
    public string WriteSheet(string pointId, string parameter, string sheetText)
    {
        if (_bundle)
        {
            string path = Path.Combine(SheetsDir, $"{_inputBase}_{_command}_bundle.txt");
            if (_bundleStarted) File.AppendAllText(path, Environment.NewLine + sheetText, Encoding.UTF8);
            else { File.WriteAllText(path, sheetText, Encoding.UTF8); _bundleStarted = true; }
            return path;
        }

        string baseName = $"{Sanitize(pointId)}__{Sanitize(parameter)}";
        string candidate = $"{baseName}.txt";
        int attempt = 1;
        while (!_usedSheetNames.Add(candidate))
            candidate = $"{baseName}({++attempt}).txt";
        string file = Path.Combine(SheetsDir, candidate);
        File.WriteAllText(file, sheetText, Encoding.UTF8);
        return file;
    }

    /// <summary>
    /// Writes the summary CSV (run-info comment header, column header, rows)
    /// and the same summary as XLSX (sheet "summary" with the table at A1,
    /// sheet "run info" with the run details; numeric-looking cells are
    /// written as numbers so Excel filters and pivots work on them).
    /// </summary>
    public void Flush(string argumentsEcho, int seriesCount)
    {
        if (_header is null) throw new InvalidOperationException("SetHeader before Flush");
        SummaryPath = Path.Combine(Root, $"{_inputBase}_{_command}_summary.csv");
        var lines = new List<string>
        {
            $"# command: {_command}",
            $"# arguments: {argumentsEcho}",
            $"# generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            $"# series: {seriesCount}",
        };
        lines.Add(CsvLine(_header));
        foreach (var row in _rows) lines.Add(CsvLine(row));
        File.WriteAllLines(SummaryPath, lines, Encoding.UTF8);

        SummaryXlsxPath = Path.ChangeExtension(SummaryPath, ".xlsx");
        var runInfo = new List<Dictionary<string, object>>
        {
            new() { ["setting"] = "command", ["value"] = _command },
            new() { ["setting"] = "arguments", ["value"] = argumentsEcho },
            new() { ["setting"] = "generated", ["value"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) },
            new() { ["setting"] = "series", ["value"] = seriesCount },
        };
        var summary = new List<Dictionary<string, object>>(_rows.Count);
        foreach (var row in _rows)
        {
            var dict = new Dictionary<string, object>();
            for (int i = 0; i < _header.Length && i < row.Length; i++)
                dict[_header[i]] = NumericOrText(row[i]);
            summary.Add(dict);
        }
        MiniExcel.SaveAs(SummaryXlsxPath, new Dictionary<string, object>
        {
            ["summary"] = summary,
            ["run info"] = runInfo,
        });
    }

    /// <summary>Invariant numeric cell when the text parses as a double, else the text.</summary>
    private static object NumericOrText(string cell)
    {
        if (cell.Length == 0) return string.Empty;
        return double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d : cell;
    }

    private string CsvLine(string[] cells)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append(_csvSep);
            string c = cells[i];
            if (c.Contains(_csvSep) || c.Contains('"') || c.Contains('\n') || c.Contains('\r'))
                sb.Append('"').Append(c.Replace("\"", "\"\"")).Append('"');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>File-system-safe name: letters/digits/._- kept, everything else '_',
    /// capped at 60 characters (output-design.md sanitization rule).</summary>
    internal static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        string s = sb.ToString();
        if (s.Length == 0) return "_";
        return s.Length > 60 ? s[..60] : s;
    }

    /// <summary>G6 invariant number, empty string for NaN/null (summary cells).</summary>
    internal static string Num(double? v) => v is double d && !double.IsNaN(d)
        ? d.ToString("G6", CultureInfo.InvariantCulture) : "";
}
