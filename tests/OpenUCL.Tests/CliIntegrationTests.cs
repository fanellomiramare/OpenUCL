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
    public void Cli_batch_out_writes_sheets_and_summary_for_ucl_btv_gof()
    {
        string? exe = FindCli();
        if (exe is null) return;   // CLI not built — skip (run dotnet build -c Release)

        var input = Path.Combine(Path.GetTempPath(), $"openucl-batch-it-{Guid.NewGuid():N}.csv");
        File.WriteAllText(input,
            "point;parameter;values\n" +
            "MW-01;TCE;\"1.2;0.8;<0.5;2.1;3.3\"\n" +
            "MW-02;TCE;\"4;5;6;7\"\n");
        var outDir = Path.Combine(Path.GetTempPath(), $"openucl-batch-it-{Guid.NewGuid():N}");

        try
        {
            foreach (var cmd in new[] { "ucl", "btv", "gof" })
            {
                var (code, output) = Run(exe, $"{cmd} \"{input}\" --out \"{outDir}\"");
                Assert.Equal(0, code);
                Assert.Contains("summary:", output);
            }

            // per-series sheets in per-command subfolders
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "ucl")).Length);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(outDir, "btv")).Length);

            // summaries: run-info header + one row per series
            string baseName = Path.GetFileNameWithoutExtension(input);
            string uclSummary = File.ReadAllText(Path.Combine(outDir, baseName + "_ucl_summary.csv"));
            Assert.StartsWith("# command: ucl", uclSummary);
            Assert.StartsWith("# series: 2", uclSummary.Split('\n')[3]);
            Assert.Contains("MW-01", uclSummary);
            Assert.Contains("recommended_method", uclSummary);

            Assert.True(File.Exists(Path.Combine(outDir, baseName + "_gof_summary.csv")));
        }
        finally
        {
            File.Delete(input);
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
