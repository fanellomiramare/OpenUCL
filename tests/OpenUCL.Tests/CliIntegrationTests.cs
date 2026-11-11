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
