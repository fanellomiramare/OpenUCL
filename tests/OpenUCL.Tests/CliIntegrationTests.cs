using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace OpenUCL.Tests;

/// <summary>
/// CLI integration test (decision #50): launches the compiled openucl binary
/// on a small packed-format fixture and asserts the expected sheet markers,
/// guarding the whole pipeline (arg parsing → readers → core → report sheet)
/// against breaks that unit tests on Core cannot see. Skips when the CLI has
/// not been built (e.g., test-only CI builds): run `dotnet build -c Release`
/// to enable it.
/// </summary>
public class CliIntegrationTests
{
    private static string? FindCli()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.Parent is not null &&
               !File.Exists(Path.Combine(dir.FullName, "OpenUCL.sln")))
            dir = dir.Parent;
        if (dir is null) return null;

        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "openucl.exe" : "openucl";
        var path = Path.Combine(dir.FullName, "src", "OpenUCL.Cli", "bin", "Release", "net8.0", exe);
        return File.Exists(path) ? path : null;
    }

    private static (int Code, string Output) Run(string exe, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(120000);
        return (p.ExitCode, output);
    }

    [Fact]
    public void Cli_test2_grouped_with_nondetects_reaches_gehan_rows()
    {
        string? exe = FindCli();
        if (exe is null) return;   // CLI not built — skip (run dotnet build -c Release)

        var file = Path.Combine(Path.GetTempPath(), $"openucl-cli-t2-{Guid.NewGuid():N}.csv");
        File.WriteAllText(file,
            "Group;Value;ND\n" +
            "A;1.5;det\nA;2.2;det\nA;2.9;det\n" +
            "B;0.8;det\nB;1.2;ND\nB;3.1;det\nB;4.2;det\n");

        try
        {
            var (code, output) = Run(exe,
                $"test2 \"{file}\" --group-col Group --group-a A --group-b B " +
                "--value-col Value --nd-flag-col ND --nd-flag-value ND");
            Assert.Equal(0, code);
            Assert.Contains("TWO-SAMPLE HYPOTHESIS TESTS", output);
            Assert.Contains("Gehan (with NDs)", output);
            Assert.Contains("Tarone-Ware (with NDs)", output);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Cli_test2_two_packed_files_classic_mode()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var fileA = Path.Combine(Path.GetTempPath(), $"openucl-cli-t2a-{Guid.NewGuid():N}.csv");
        var fileB = Path.Combine(Path.GetTempPath(), $"openucl-cli-t2b-{Guid.NewGuid():N}.csv");
        File.WriteAllText(fileA, "Point;Parameter;Values\nMW-A;Cu;\"1.5;2.2;2.9\"\n");
        File.WriteAllText(fileB, "Point;Parameter;Values\nMW-B;Cu;\"0.8;1.2;3.1;4.2\"\n");

        try
        {
            var (code, output) = Run(exe, $"test2 \"{fileA}\" \"{fileB}\"");
            Assert.Equal(0, code);
            Assert.Contains("TWO-SAMPLE HYPOTHESIS TESTS", output);
            Assert.Contains("Wilcoxon-Mann-Whitney", output);
        }
        finally
        {
            File.Delete(fileA);
            File.Delete(fileB);
        }
    }

    [Fact]
    public void Cli_batch_out_writes_sheets_and_summary_for_all_sheet_commands()
    {
        string? exe = FindCli();
        if (exe is null) return;   // CLI not built — skip (run dotnet build -c Release)

        var input = Path.Combine(Path.GetTempPath(), $"openucl-batch-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input,
            "point;parameter;values\n" +
            "MW-01;TCE;\"1.2;0.8;<0.5;2.1;3.3\"\n" +
            "MW-02;TCE;\"4;5;6;7\"\n");
        var longInput = Path.Combine(Path.GetTempPath(), $"openucl-batch-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(longInput, "Group;Value\nA;1\nA;2\nA;3\nB;5\nB;6\nB;7\n");
        var outDir = Path.Combine(Path.GetTempPath(), $"openucl-batch-it-{Guid.NewGuid():N}");

        try
        {
            var baseName = Path.GetFileNameWithoutExtension(input);
            var longBaseName = Path.GetFileNameWithoutExtension(longInput);
            foreach (var (cmd, cmdArgs, summaryName) in new[] {
                ("ucl", $"\"{input}\"", baseName),
                ("btv", $"\"{input}\"", baseName),
                ("gof", $"\"{input}\"", baseName),
                ("ndstats", $"\"{input}\"", baseName),
                ("test1", $"\"{input}\" --cs 4", baseName),
                ("outlier", $"\"{input}\"", baseName),
                ("trend", $"\"{input}\"", baseName),
                ("anova", $"\"{longInput}\" --long --group-col Group --value-col Value", longBaseName),
            })
            {
                var (code, output) = Run(exe, $"{cmd} {cmdArgs} --out \"{outDir}\"");
                Assert.Equal(0, code);
                Assert.True(File.Exists(Path.Combine(outDir, summaryName + $"_{cmd}_summary.csv")),
                    $"missing summary for {cmd}");
            }

            // per-series sheets in per-command subfolders (ucl/btv have 2 series each)
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "ucl")).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "btv")).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "test1")).Length);
            Assert.Single(Directory.GetFiles(Path.Combine(outDir, "anova")));

            // summaries: run-info header + one row per series; XLSX alongside
            Assert.True(File.Exists(Path.Combine(outDir, baseName + "_ucl_summary.xlsx")));
            string uclSummary = File.ReadAllText(Path.Combine(outDir, baseName + "_ucl_summary.csv"));
            Assert.StartsWith("# command: ucl", uclSummary);
            Assert.StartsWith("# series: 2", uclSummary.Split('\n')[3]);
            Assert.Contains("MW-01", uclSummary);
            Assert.Contains("recommended_method", uclSummary);
            string anovaSummary = File.ReadAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(longInput) + "_anova_summary.csv"));
            Assert.StartsWith("# command: anova", anovaSummary);
            Assert.Contains("kw_h", anovaSummary);
        }
        finally
        {
            File.Delete(input);
            File.Delete(longInput);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Cli_batch_bundle_writes_one_combined_file()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var input = Path.Combine(Path.GetTempPath(), $"openucl-bundle-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input,
            "point;parameter;values\n" +
            "MW-01;TCE;\"1.2;0.8;2.1;3.3\"\n" +
            "MW-02;TCE;\"4;5;6;7\"\n");
        var outDir = Path.Combine(Path.GetTempPath(), $"openucl-bundle-it-{Guid.NewGuid():N}");

        try
        {
            var (code, output) = Run(exe, $"ucl \"{input}\" --out \"{outDir}\" --bundle");
            Assert.Equal(0, code);
            Assert.Contains("summary:", output);

            var sheets = Directory.GetFiles(Path.Combine(outDir, "ucl"));
            Assert.Single(sheets);
            string text = File.ReadAllText(sheets[0]);
            Assert.Contains("MW-01", text);
            Assert.Contains("MW-02", text);
        }
        finally
        {
            File.Delete(input);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Cli_test2_single_file_two_value_columns()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var input = Path.Combine(Path.GetTempPath(), $"openucl-t2col-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input,
            "Point;Pre;Post\n" +
            "P1;1.5;2.1\nP1;2.2;2.4\nP1;2.9;3.1\n" +
            "P2;0.8;1.9\nP2;1.2;2.6\nP2;3.1;3.4\n");

        try
        {
            var (code, output) = Run(exe, $"test2 \"{input}\" --value-col Pre --value-col Post");
            Assert.Equal(0, code);
            Assert.Contains("TWO-SAMPLE HYPOTHESIS TESTS", output);
            Assert.Contains("column 'Pre' (n=6) vs column 'Post' (n=6)", output);
            Assert.Contains("Wilcoxon-Mann-Whitney", output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    /// <summary>
    /// Audit 2026-11-18 [E1]: a constant series (all values identical, e.g. all at
    /// the reporting value) ended gof/ucl/btv with an unhandled exception — the
    /// following series were never processed and no summary was written. Now it
    /// becomes a summary row with the reason in "warnings" and the batch goes on.
    /// </summary>
    [Fact]
    public void Cli_batch_survives_a_constant_series()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var input = Path.Combine(Path.GetTempPath(), $"openucl-const-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input,
            "point;parameter;values\n" +
            "MW-01;TCE;\"5;5;5;5;5;5\"\n" +
            "MW-02;TCE;\"1.2;0.8;2.1;3.3;1.9;2.4\"\n");
        var outDir = Path.Combine(Path.GetTempPath(), $"openucl-const-it-{Guid.NewGuid():N}");
        try
        {
            foreach (var cmd in new[] { "ucl", "btv", "gof" })
            {
                var (code, output) = Run(exe, $"{cmd} \"{input}\" --out \"{outDir}\"");
                Assert.True(code == 0, $"{cmd} exited with {code}: {output}");
                var lines = File.ReadAllLines(Path.Combine(outDir,
                    Path.GetFileNameWithoutExtension(input) + $"_{cmd}_summary.csv"));
                var rows = lines.Where(l => l.StartsWith("MW-")).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Contains("identical", rows[0]);
                Assert.StartsWith("MW-02", rows[1]);
            }
            var (codeText, text) = Run(exe, $"ucl \"{input}\"");
            Assert.Equal(0, codeText);
            Assert.Contains("MW-02", text);
        }
        finally
        {
            File.Delete(input);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>
    /// Audit 2026-11-18 [E1]: ucl crashed on a series with more than 5000 detects
    /// (Shapiro-Wilk limit) and gof printed "not normal" without testing. ProUCL
    /// performs S-W up to n = 2000 (TG 2.2.1.2) and judges larger series by
    /// Lilliefors alone — both commands now do the same.
    /// </summary>
    [Fact]
    public void Cli_handles_series_beyond_the_shapiro_wilk_range()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var values = Enumerable.Range(1, 5200)
            .Select(i => (Math.Exp(Math.Sin(i * 0.7)) + (i % 13) * 0.1).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        var input = Path.Combine(Path.GetTempPath(), $"openucl-large-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input, "point;parameter;values\nLOGGER;T;" + string.Join("|", values) + "\n");
        try
        {
            var (code, output) = Run(exe, $"ucl \"{input}\"");
            Assert.True(code == 0, output);
            Assert.Contains("not performed for n > 2000", output);
            Assert.Contains("RECOMMENDATION", output);

            var (codeGof, outGof) = Run(exe, $"gof \"{input}\"");
            Assert.True(codeGof == 0, outGof);
            Assert.Contains("Lilliefors alone", outGof);
        }
        finally
        {
            File.Delete(input);
        }
    }

    /// <summary>
    /// Audit 2026-11-18 [E2]: at 97.5% the adjusted gamma UCL used the 95% table
    /// row (beta 0.0395 > alpha) and returned the 95% value as the recommended
    /// UCL. ProUCL supports it only at 90/95/99% (TG 2.4); ProUCL recommends only
    /// at 95% (User Guide).
    /// </summary>
    [Fact]
    public void Cli_ucl_adjusted_gamma_only_at_supported_levels()
    {
        string? exe = FindCli();
        if (exe is null) return;

        var input = Path.Combine(Path.GetTempPath(), $"openucl-x22-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input, "point;parameter;values\nExample2-2;X;\"0.3489|0.8526|2.5445|2.5602|3.3706|" +
            "4.8911|5.0930|5.6408|7.0407|14.1715|15.2608|17.6214|18.7690|23.6804|25.0461|31.7720|60.7066|" +
            "67.0926|72.6243|78.8357|80.0867|113.0230|117.0360|164.3302|169.8303\"\n");
        try
        {
            var (code95, out95) = Run(exe, $"ucl \"{input}\"");
            Assert.Equal(0, code95);
            Assert.Contains("adjusted gamma UCL = 74.2685", out95);   // TG Table 2-6: 74.27

            var (code975, out975) = Run(exe, $"ucl \"{input}\" --alpha 0.025");
            Assert.Equal(0, code975);
            Assert.Contains("adjusted gamma UCL not available at 97.5% confidence", out975);
            Assert.DoesNotContain("74.2685", out975);
            Assert.Contains("suggests UCLs only at 95% confidence", out975);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public void Cli_pipeline_produces_expected_sheet_markers()
    {
        string? exe = FindCli();
        if (exe is null) return;   // CLI not built — skip (run dotnet build -c Release)

        var file = Path.Combine(Path.GetTempPath(), $"openucl-cli-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(file,
            "Point;Parameter;Values\n" +
            "MW-01;TCE;\"1.2;0.8;<0.5;2.1;3.3\"\n" +
            "MW-02;TCE;\"4;5;6;7\"\n");

        try
        {
            var (codeSummary, outSummary) = Run(exe, $"summary \"{file}\"");
            Assert.Equal(0, codeSummary);
            Assert.Contains("TCE", outSummary);
            Assert.Contains("MW-01", outSummary);

            var (codeUcl, outUcl) = Run(exe, $"ucl \"{file}\"");
            Assert.Equal(0, codeUcl);
            Assert.Contains("UCL / EPC ESTIMATION", outUcl);
            Assert.Contains("RECOMMENDATION", outUcl);

            var (codeBtv, outBtv) = Run(exe, $"btv \"{file}\"");
            Assert.Equal(0, codeBtv);
            Assert.Contains("BACKGROUND THRESHOLD VALUES", outBtv);
            Assert.Contains("d2max (for USL)", outBtv);
            Assert.Contains("USL normal", outBtv);
            // MW-01 carries an ND: the KM-based ND section must appear
            Assert.Contains("BTVS ACCOUNTING FOR NONDETECTS", outBtv);
            Assert.Contains("USL normal-KM", outBtv);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
