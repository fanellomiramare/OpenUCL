using System.Globalization;
using OpenUCL.Core;
using OpenUCL.Data;

namespace OpenUCL.Cli;

internal static class Program
{
    private const string Version = "0.1.0";

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }
        if (args[0] is "--version")
        {
            Console.WriteLine($"openucl {Version}");
            return 0;
        }

        try
        {
            return args[0] switch
            {
                "summary" => RunSummary(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or FormatException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 3;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"error: unknown command '{cmd}'.");
        PrintUsage();
        return 2;
    }

    private static int RunSummary(string[] args)
    {
        string? file = null;
        char? columnSep = null, valueSep = null;
        bool hasHeader = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--col-sep" when i + 1 < args.Length:
                    columnSep = ParseSeparator(args[++i], "--col-sep"); break;
                case "--value-sep" when i + 1 < args.Length:
                    valueSep = ParseSeparator(args[++i], "--value-sep"); break;
                case "--no-header":
                    hasHeader = false; break;
                default:
                    if (file is null) file = args[i];
                    else { Console.Error.WriteLine($"error: unexpected argument '{args[i]}'."); return 2; }
                    break;
            }
        }
        if (file is null) { PrintUsage(); return 2; }

        var options = new PackedSeriesReadOptions
        {
            ColumnSeparator = columnSep ?? ';',
            ValueSeparator = valueSep,
            HasHeader = hasHeader,
        };

        Console.WriteLine($"{"point",-12} {"parameter",-10} {"N",6} {"ND",6} {"min",12} {"mean",12} {"max",12} {"sd",12}");
        Console.WriteLine(new string('-', 88));

        int series = 0, observations = 0;
        foreach (var s in PackedSeriesReader.ReadFile(file, options))
        {
            double[] detected = s.DetectedValues();
            SampleSummary? stats = detected.Length > 0 ? Descriptive.Compute(detected) : null;
            string Min = stats?.Min.ToString("G6", CultureInfo.InvariantCulture) ?? "-";
            string Mean = stats?.Mean.ToString("G6", CultureInfo.InvariantCulture) ?? "-";
            string Max = stats?.Max.ToString("G6", CultureInfo.InvariantCulture) ?? "-";
            string Sd = stats is { N: > 1 } ? stats.StandardDeviation.ToString("G6", CultureInfo.InvariantCulture) : "-";
            Console.WriteLine($"{Truncate(s.PointId, 12),-12} {Truncate(s.Parameter, 10),-10} {s.N,6} {s.NondetectCount,6} {Min,12} {Mean,12} {Max,12} {Sd,12}");
            series++;
            observations += s.N;
        }

        Console.WriteLine(new string('-', 88));
        Console.WriteLine($"{series} series, {observations} observations (nondetects included in N; stats on detected values only).");
        return 0;
    }

    private static char ParseSeparator(string value, string optionName)
    {
        var v = value switch
        {
            "\\t" or "tab" => '\t',
            _ => value.Length == 1 ? value[0] : throw new FormatException($"{optionName} must be a single character, got '{value}'"),
        };
        return v;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "~";

    private static void PrintUsage()
    {
        Console.WriteLine($"""
            openucl {Version} - open-source reimplementation of ProUCL 5.2 statistical methods
            (not affiliated with or endorsed by the U.S. EPA)

            usage: openucl <command> [arguments] [options]

            commands:
              summary <file>        descriptive statistics for every series in a packed file

            options:
              --col-sep <char>      column separator (default ';')
              --value-sep <char>    packed value separator (default: auto-detect, ';' '|' ',' tab)
              --no-header           the file has no header row
              -h, --help            show this help

            example:
              openucl summary wells.csv --value-sep |
            """);
    }
}
