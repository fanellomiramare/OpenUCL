using OpenUCL.Cli;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// Batch output unit tests (Phase 4, output-design.md / decision #72): folder
/// layout, filename sanitization + collision disambiguation, summary CSV with
/// the run-info header, and the bundle mode.
/// </summary>
public class BatchOutputTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"openucl-batch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Sanitize_keeps_safe_characters_and_replaces_the_rest()
    {
        Assert.Equal("MW-01_a_B.", OpenUCL.Cli.BatchRun.Sanitize("MW-01/a B."));
        Assert.Equal("___", OpenUCL.Cli.BatchRun.Sanitize("///"));
        Assert.Equal("_", OpenUCL.Cli.BatchRun.Sanitize(""));
        Assert.Equal(60, OpenUCL.Cli.BatchRun.Sanitize(new string('x', 100)).Length);
    }

    [Fact]
    public void Sheet_names_are_sanitized_and_disambiguated_on_collision()
    {
        var dir = TempDir();
        try
        {
            var batch = new OpenUCL.Cli.BatchRun(dir, "ucl", "wells.csv", bundle: false);
            batch.WriteSheet("MW-01/a", "TCE", "sheet one");
            batch.WriteSheet("MW-01_a", "TCE", "sheet two");   // collides after sanitization

            var names = Directory.GetFiles(Path.Combine(dir, "ucl"))
                .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
            Assert.Equal(2, names.Count);
            Assert.Contains("MW-01_a__TCE.txt", names);
            Assert.Contains("MW-01_a__TCE(2).txt", names);
            Assert.Equal("sheet two", File.ReadAllText(Path.Combine(dir, "ucl", "MW-01_a__TCE(2).txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Summary_has_run_info_header_and_rows()
    {
        var dir = TempDir();
        try
        {
            var batch = new OpenUCL.Cli.BatchRun(dir, "btv", "wells.csv", bundle: false);
            batch.SetHeader("point", "parameter", "n", "upl1");
            batch.AddRow("MW-01", "TCE", "12", "1.5");
            batch.AddRow("MW-02", "TCE", "9", "3.25");
            batch.Flush("btv wells.csv", 2);

            Assert.True(File.Exists(batch.SummaryPath));
            Assert.EndsWith($"wells_btv_summary.csv", Path.GetFileName(batch.SummaryPath));
            string[] lines = File.ReadAllLines(batch.SummaryPath);
            Assert.StartsWith("# command: btv", lines[0]);
            Assert.StartsWith("# arguments: btv wells.csv", lines[1]);
            Assert.StartsWith("# generated:", lines[2]);
            Assert.StartsWith("# series: 2", lines[3]);
            Assert.Equal("point,parameter,n,upl1", lines[4]);
            Assert.Equal("MW-01,TCE,12,1.5", lines[5]);
            Assert.Equal("MW-02,TCE,9,3.25", lines[6]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Csv_fields_containing_the_separator_are_quoted()
    {
        var dir = TempDir();
        try
        {
            var batch = new OpenUCL.Cli.BatchRun(dir, "gof", "w.csv", bundle: false);
            batch.SetHeader("point", "warnings");
            batch.AddRow("P1", "2: fewer than 3 detects, BTVs skipped, with, comma");
            batch.Flush("gof w.csv", 1);
            string[] lines = File.ReadAllLines(batch.SummaryPath);
            Assert.Equal("P1,\"2: fewer than 3 detects, BTVs skipped, with, comma\"", lines[^1]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Bundle_mode_appends_all_sheets_to_one_file()
    {
        var dir = TempDir();
        try
        {
            var batch = new OpenUCL.Cli.BatchRun(dir, "ucl", "wells.csv", bundle: true);
            batch.WriteSheet("A", "P", "== sheet A ==");
            batch.WriteSheet("B", "P", "== sheet B ==");
            var files = Directory.GetFiles(Path.Combine(dir, "ucl"));
            Assert.Single(files);
            string text = File.ReadAllText(files[0]);
            Assert.Contains("== sheet A ==", text);
            Assert.Contains("== sheet B ==", text);
            Assert.True(text.IndexOf("== sheet A ==", StringComparison.Ordinal)
                     < text.IndexOf("== sheet B ==", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Flush_writes_xlsx_with_summary_and_run_info_sheets()
    {
        var dir = TempDir();
        try
        {
            var batch = new OpenUCL.Cli.BatchRun(dir, "btv", "wells.csv", bundle: false);
            batch.SetHeader("point", "parameter", "n", "upl1");
            batch.AddRow("MW-01", "TCE", "12", "1.5");
            batch.AddRow("MW-02", "TCE", "9", "not-a-number");
            batch.Flush("btv wells.csv", 2);

            Assert.True(File.Exists(batch.SummaryXlsxPath));
            Assert.EndsWith("wells_btv_summary.xlsx", Path.GetFileName(batch.SummaryXlsxPath));

            // summary sheet: header at row 1, numeric cells parsed as numbers
            var rows = MiniExcelLibs.MiniExcel
                .Query(batch.SummaryXlsxPath, useHeaderRow: true, sheetName: "summary").ToList();
            Assert.Equal(2, rows.Count);
            var row0 = (IDictionary<string, object>)rows[0];
            Assert.Equal("MW-01", row0["point"]);
            Assert.Equal(12.0, (double)row0["n"], 10);
            Assert.Equal(1.5, (double)row0["upl1"], 10);
            var row1 = (IDictionary<string, object>)rows[1];
            Assert.Equal("not-a-number", row1["upl1"]);   // non-numeric stays text

            // run info sheet
            var info = MiniExcelLibs.MiniExcel
                .Query(batch.SummaryXlsxPath, useHeaderRow: true, sheetName: "run info").ToList();
            Assert.True(info.Count >= 4);
            Assert.Equal("command", (string)((IDictionary<string, object>)info[0])["setting"]);
            Assert.Equal("btv", (string)((IDictionary<string, object>)info[0])["value"]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
