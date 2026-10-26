using System.Globalization;
using OpenUCL.Core;
using OpenUCL.Data;

namespace OpenUCL.Cli;

internal static class Program
{
    private const string Version = "0.2.0";

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
                "gof" => RunGof(args[1..]),
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

    // ---- input specification shared by all commands ----

    private sealed class InputSpec
    {
        public string File = "";
        public bool LongFormat;
        public char? ColumnSep;
        public char? ValueSep;
        public bool NoHeader;
        public List<string> ValueColumns = new();
        public string? GroupColumn;
        public string? NdFlagColumn;
        public string NdFlagValue = "ND";
        public string? DlColumn;

        public static InputSpec Parse(string[] args)
        {
            var spec = new InputSpec();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--col-sep" when i + 1 < args.Length:
                        spec.ColumnSep = ParseSeparator(args[++i], "--col-sep"); break;
                    case "--value-sep" when i + 1 < args.Length:
                        spec.ValueSep = ParseSeparator(args[++i], "--value-sep"); break;
                    case "--no-header":
                        spec.NoHeader = true; break;
                    case "--long":
                        spec.LongFormat = true; break;
                    case "--value-col" when i + 1 < args.Length:
                        spec.ValueColumns.Add(args[++i]); break;
                    case "--group-col" when i + 1 < args.Length:
                        spec.GroupColumn = args[++i]; break;
                    case "--nd-flag-col" when i + 1 < args.Length:
                        spec.NdFlagColumn = args[++i]; break;
                    case "--nd-flag-value" when i + 1 < args.Length:
                        spec.NdFlagValue = args[++i]; break;
                    case "--dl-col" when i + 1 < args.Length:
                        spec.DlColumn = args[++i]; break;
                    default:
                        if (spec.File.Length == 0) spec.File = args[i];
                        else throw new FormatException($"unexpected argument '{args[i]}'");
                        break;
                }
            }
            if (spec.File.Length == 0) throw new FormatException("no input file given");
            return spec;
        }

        public IEnumerable<SeriesRecord> ReadSeries()
        {
            bool isSpreadsheet = Path.GetExtension(File).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls";
            if (!LongFormat && !isSpreadsheet)
            {
                var packedOptions = new PackedSeriesReadOptions
                {
                    ColumnSeparator = ColumnSep ?? ';',
                    ValueSeparator = ValueSep,
                    HasHeader = !NoHeader,
                };
                return PackedSeriesReader.ReadFile(File, packedOptions);
            }

            var tableOptions = new LongTableReadOptions
            {
                ColumnSeparator = ColumnSep ?? ';',
                HasHeader = !NoHeader,
                NdFlagColumn = NdFlagColumn,
                NdFlagValue = NdFlagValue,
                DetectionLimitColumn = DlColumn,
            };
            var table = LongTableReader.ReadTable(File, tableOptions);
            var valueColumns = ValueColumns.Count > 0
                ? ValueColumns
                : table.Columns.Where(c => IsNumericColumn(table, c)).ToList();
            if (valueColumns.Count == 0)
                throw new FormatException("no numeric value column found; use --value-col to select one");
            return LongTableReader.ToSeries(table,
                new SeriesSelection { ValueColumns = valueColumns, GroupColumn = GroupColumn },
                tableOptions);
        }

        private static bool IsNumericColumn(LongTable table, string column)
        {
            int col = table.ColumnIndex(column);
            int checked_ = 0;
            for (int row = 0; row < table.RowCount && checked_ < 100; row++)
            {
                if (table.GetDouble(row, col) is not null) return true;
                if (table.GetString(row, col).Trim().Length > 0) checked_++;
            }
            return false;
        }
    }

    private static char ParseSeparator(string value, string optionName)
    {
        return value switch
        {
            "\\t" or "tab" => '\t',
            _ => value.Length == 1 ? value[0]
                : throw new FormatException($"{optionName} must be a single character, got '{value}'"),
        };
    }

    // ---- commands ----

    private static int RunSummary(string[] args)
    {
        var spec = InputSpec.Parse(args);
        Console.WriteLine($"{"point",-12} {"parameter",-10} {"N",6} {"ND",6} {"min",12} {"mean",12} {"max",12} {"sd",12}");
        Console.WriteLine(new string('-', 88));

        int series = 0, observations = 0;
        foreach (var s in spec.ReadSeries())
        {
            double[] detected = s.DetectedValues();
            SampleSummary? stats = detected.Length > 0 ? Descriptive.Compute(detected) : null;
            string Min = F(stats?.Min), Mean = F(stats?.Mean), Max = F(stats?.Max);
            string Sd = stats is { N: > 1 } ? F(stats.StandardDeviation) : "-";
            Console.WriteLine($"{Truncate(s.PointId, 12),-12} {Truncate(s.Parameter, 10),-10} {s.N,6} {s.NondetectCount,6} {Min,12} {Mean,12} {Max,12} {Sd,12}");
            series++;
            observations += s.N;
        }

        Console.WriteLine(new string('-', 88));
        Console.WriteLine($"{series} series, {observations} observations (nondetects included in N; stats on detected values only).");
        return 0;
    }

    private static int RunGof(string[] args)
    {
        double alpha = 0.05;
        bool lognormal = false, gamma = false;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha" when i + 1 < args.Length:
                    alpha = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--lognormal":
                    lognormal = true; break;
                case "--gamma":
                    gamma = true; break;
                default:
                    rest.Add(args[i]); break;
            }
        }
        if (gamma && lognormal)
            throw new FormatException("--gamma and --lognormal are mutually exclusive");
        if (alpha is < 0.005 or > 0.495)
            throw new FormatException("--alpha must be in (0.005, 0.495)");

        return gamma ? RunGammaGof(rest.ToArray(), alpha) : RunNormalGof(rest.ToArray(), alpha, lognormal);
    }

    private static int RunNormalGof(string[] args, double alpha, bool lognormal)
    {
        string distribution = lognormal ? "lognormal" : "normal";
        Console.WriteLine($"goodness-of-fit, {distribution} distribution, alpha = {alpha}");
        Console.WriteLine($"{"point",-10} {"parameter",-8} {"N",5} {"ND",4} {"S-W W",8} {"p",8} {"W-crit",8} {"Lill D",8} {"D-crit",8} conclusion");
        Console.WriteLine(new string('-', 92));

        int series = 0;
        foreach (var s in InputSpec.Parse(args).ReadSeries())
        {
            double[] detected = s.DetectedValues();
            if (lognormal)
            {
                int nonPositive = detected.Count(v => v <= 0);
                if (nonPositive > 0)
                    Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: {nonPositive} non-positive values excluded from lognormal GOF.");
                detected = detected.Where(v => v > 0).Select(v => Math.Log(v)).ToArray();
            }

            string w = "-", p = "-", wc = "-", d = "-", dc = "-";
            GofConclusion conclusion = GofConclusion.NotNormal;
            if (detected.Length is >= 3 and <= 5000)
            {
                var sw = GoodnessOfFit.ShapiroWilk(detected);
                var lf = GoodnessOfFit.Lilliefors(detected);
                conclusion = GoodnessOfFit.Conclude(sw, lf, alpha);
                w = F(sw.W);
                p = sw.PValue switch { < 1e-4 => "<1e-4", _ => F(sw.PValue) };
                wc = F(sw.CriticalValue(alpha));
                d = F(lf.D);
                dc = F(lf.CriticalValue(alpha));
            }
            string conclusionText = conclusion switch
            {
                GofConclusion.Normal => $"{distribution}",
                GofConclusion.ApproximatelyNormal => $"approx. {distribution}",
                _ => $"not {distribution}",
            };
            Console.WriteLine($"{Truncate(s.PointId, 10),-10} {Truncate(s.Parameter, 8),-8} {detected.Length,5} {s.NondetectCount,4} {w,8} {p,8} {wc,8} {d,8} {dc,8} {conclusionText}");
            series++;
        }
        Console.WriteLine(new string('-', 92));
        Console.WriteLine($"{series} series. S-W = Shapiro-Wilk (Royston AS R94); Lill = Lilliefors K-S");
        Console.WriteLine("(D-crit for n<=50 uses the large-sample formula: ProUCL's exact Moling-Abdi table pending).");
        return 0;
    }

    private static int RunGammaGof(string[] args, double requestedAlpha)
    {
        // Gamma critical values are tabulated only at 0.10, 0.05, 0.01 (as in ProUCL).
        double alpha = requestedAlpha <= 0.02 ? 0.01 : requestedAlpha <= 0.075 ? 0.05 : 0.10;
        if (alpha != requestedAlpha)
            Console.WriteLine($"note: gamma critical values are tabulated at 0.10/0.05/0.01; using alpha = {alpha}.");

        Console.WriteLine($"goodness-of-fit, gamma distribution, alpha = {alpha}");
        Console.WriteLine($"{"point",-10} {"parameter",-8} {"N",5} {"ND",4} {"k-hat",8} {"theta",8} {"A-D A2",8} {"A2-crit",8} {"KS D",8} {"D-crit",8} conclusion");
        Console.WriteLine(new string('-', 104));

        int series = 0;
        foreach (var s in InputSpec.Parse(args).ReadSeries())
        {
            double[] detected = s.DetectedValues();
            string kh = "-", th = "-", a2 = "-", ac = "-", d = "-", dc = "-";
            string conclusionText = "not gamma";
            bool acceptAd = false, acceptKs = false;
            if (detected.Length >= 3 && detected.All(v => v > 0))
            {
                try
                {
                    var g = GammaGof.Compute(detected);
                    var adCrit = g.CriticalValue(GammaGofTest.AndersonDarling, alpha);
                    var ksCrit = g.CriticalValue(GammaGofTest.KolmogorovSmirnov, alpha);
                    acceptAd = g.AD_A2 <= adCrit;
                    acceptKs = g.KS_D <= ksCrit;
                    kh = F(g.KHat);
                    th = F(g.ThetaHat);
                    a2 = F(g.AD_A2);
                    ac = F(adCrit);
                    d = F(g.KS_D);
                    dc = F(ksCrit);
                    conclusionText = (acceptAd, acceptKs) switch
                    {
                        (true, true) => "gamma",
                        (false, false) => "not gamma",
                        _ => "approx. gamma",
                    };
                }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: {ex.Message}");
                }
            }
            else if (detected.Length >= 3)
            {
                Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: non-positive values present, gamma GOF not applicable.");
            }

            Console.WriteLine($"{Truncate(s.PointId, 10),-10} {Truncate(s.Parameter, 8),-8} {detected.Length,5} {s.NondetectCount,4} {kh,8} {th,8} {a2,8} {ac,8} {d,8} {dc,8} {conclusionText}");
            series++;
        }
        Console.WriteLine(new string('-', 104));
        Console.WriteLine($"{series} series. Critical values: Singh, Singh and Iaci (2002), ProUCL 5.2 Technical Guide Appendix A.");
        return 0;
    }

    private static string F(double? v) => v?.ToString("G6", CultureInfo.InvariantCulture) ?? "-";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "~";

    private static void PrintUsage()
    {
        Console.WriteLine($"""
            openucl {Version} - open-source reimplementation of ProUCL 5.2 statistical methods
            (not affiliated with or endorsed by the U.S. EPA)

            usage: openucl <command> [arguments] [options]

            commands:
              summary <file>        descriptive statistics for every series
              gof <file>            goodness-of-fit for every series (default: Shapiro-Wilk +
                                    Lilliefors; --gamma for the gamma EDF tests)

            input formats:
              packed (default)      one series per row: point, parameter, "v1;v2;..."
              long / xlsx           ProUCL layout: one observation per row (--long, or .xlsx file)

            options:
              --col-sep <char>      column separator (default ';')
              --value-sep <char>    packed value separator (default: auto-detect ';' '|' ',' tab)
              --no-header           the file has no header row
              --long                read the file in long format
              --value-col <name>    long format: value column (repeatable; default: all numeric columns)
              --group-col <name>    long format: group column (one series per group)
              --nd-flag-col <name>  long format: nondetect flag column
              --nd-flag-value <s>   long format: flag value marking nondetects (default ND)
              --dl-col <name>       long format: detection limit column (default: value column holds DL)
              --alpha <x>           gof: significance level (default 0.05)
              --lognormal           gof: test the log-transformed data
              --gamma               gof: gamma EDF tests (A-D and K-S, Appendix A critical values)
              -h, --help            show this help

            examples:
              openucl summary wells.csv --value-sep |
              openucl summary data.xlsx --group-col Well --nd-flag-col C
              openucl gof data.xlsx --value-col TCE --group-col Well --alpha 0.05
            """);
    }
}
