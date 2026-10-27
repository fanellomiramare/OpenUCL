using System.Globalization;
using OpenUCL.Core;
using OpenUCL.Data;

namespace OpenUCL.Cli;

internal static class Program
{
    private const string Version = "0.7.0";

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
                "ndstats" => RunNdStats(args[1..]),
                "ucl" => RunUcl(args[1..]),
                "btv" => RunBtv(args[1..]),
                "dump" => RunDump(args[1..]),
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
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (NdFlagColumn is not null) excluded.Add(NdFlagColumn);
            if (DlColumn is not null) excluded.Add(DlColumn);
            var valueColumns = ValueColumns.Count > 0
                ? ValueColumns
                : table.Columns.Where(c => !excluded.Contains(c) && IsNumericColumn(table, c)).ToList();
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

    private static int RunNdStats(string[] args)
    {
        Console.WriteLine($"{"point",-9} {"parameter",-8} {"N",4} {"ND",4} {"%ND",6} {"det.mean",10} {"det.sd",10} {"DL/2 mean",10} {"KM mean",10} {"KM SE",10} {"KM sd",10} {"LROS mean",10} {"LROS sd",10} {"GROS mean",10} {"GROS sd",10}");
        Console.WriteLine(new string('-', 154));

        int series = 0;
        foreach (var s in InputSpec.Parse(args).ReadSeries())
        {
            string dl2m = "-", km = "-", kmse = "-", kmsd = "-", lm = "-", ls = "-", gm = "-", gs = "-";
            try
            {
                var dl2 = Descriptive.Compute(Censored.DlHalf(s.Observations));
                dl2m = F(dl2.Mean);
            }
            catch (ArgumentException) { }

            if (s.Observations.Count > 0)
            {
                try
                {
                    var km2 = Censored.KaplanMeier(s.Observations);
                    km = F(km2.Mean);
                    kmse = double.IsNaN(km2.StandardErrorOfMean) ? "-" : F(km2.StandardErrorOfMean);
                    kmsd = F(km2.Sd);
                }
                catch (ArgumentException) { }

                if (s.NondetectCount > 0)
                {
                    try
                    {
                        var lros = Censored.RosLognormal(s.Observations);
                        var st = Descriptive.Compute(lros.FullData);
                        lm = F(st.Mean);
                        ls = F(st.StandardDeviation);
                    }
                    catch (ArgumentException ex) { Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: LROS: {ex.Message}"); }

                    try
                    {
                        var gros = Censored.RosGamma(s.Observations);
                        var st = Descriptive.Compute(gros.FullData);
                        gm = F(st.Mean);
                        gs = F(st.StandardDeviation);
                        if (gros.NegativeImputations)
                            Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: GROS produced non-positive imputed NDs (documented limitation; prefer KM or LROS).");
                    }
                    catch (ArgumentException ex) { Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: GROS: {ex.Message}"); }
                }
            }

            double[] detected = s.DetectedValues();
            SampleSummary? det = detected.Length > 0 ? Descriptive.Compute(detected) : null;
            string detSd = det is { N: > 1 } ? F(det.StandardDeviation) : "-";
            double pctNd = s.N > 0 ? 100.0 * s.NondetectCount / s.N : 0;

            Console.WriteLine($"{Truncate(s.PointId, 9),-9} {Truncate(s.Parameter, 8),-8} {s.N,4} {s.NondetectCount,4} {pctNd,5:G4}% {F(det?.Mean),10} {detSd,10} {dl2m,10} {km,10} {kmse,10} {kmsd,10} {lm,10} {ls,10} {gm,10} {gs,10}");
            series++;
        }
        Console.WriteLine(new string('-', 154));
        Console.WriteLine($"{series} series. KM = Kaplan-Meier (TG eq. 4-1..4-3); LROS/GROS = robust ROS (TG section 4.5); DL/2 substitution.");
        return 0;
    }

    private static int RunDump(string[] args)
    {
        var spec = InputSpec.Parse(args);
        bool isSpreadsheet = Path.GetExtension(spec.File).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls";
        var tableOptions = new LongTableReadOptions
        {
            ColumnSeparator = spec.ColumnSep ?? ';',
            HasHeader = !spec.NoHeader,
        };
        var table = isSpreadsheet || spec.LongFormat
            ? LongTableReader.ReadTable(spec.File, tableOptions)
            : throw new FormatException("dump reads long-format tables (csv/tsv/xlsx/xls)");

        Console.WriteLine($"columns ({table.Columns.Count}): {string.Join(" | ", table.Columns)}");
        Console.WriteLine($"rows: {table.RowCount}");
        for (int r = 0; r < table.RowCount; r++)
        {
            var cells = new string[table.Columns.Count];
            for (int c = 0; c < table.Columns.Count; c++)
            {
                var d = table.GetDouble(r, c);
                cells[c] = d is not null ? d.Value.ToString("G6", CultureInfo.InvariantCulture)
                                         : $"'{table.GetString(r, c)}'";
            }
            Console.WriteLine($"{r + 1,4}: {string.Join(" | ", cells)}");
        }
        return 0;
    }

    private static int RunUcl(string[] args)
    {
        double cc = 0.95;
        int boot = Ucl.DefaultBootstrapRuns;
        ulong seed = Ucl.DefaultBootstrapSeed;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha" when i + 1 < args.Length:
                    cc = 1.0 - double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--boot" when i + 1 < args.Length:
                    boot = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--seed" when i + 1 < args.Length:
                    seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default:
                    rest.Add(args[i]); break;
            }
        }
        if (cc is < 0.5 or >= 1.0) throw new FormatException("--alpha must be in (0.005, 0.495)");
        var spec = InputSpec.Parse(rest.ToArray());
        string fileHash = ShortHash(spec.File);

        int series = 0;
        foreach (var s in spec.ReadSeries())
        {
            double[] detected = s.DetectedValues();
            if (detected.Length < 3)
            {
                Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: fewer than 3 detects, UCLs skipped.");
                continue;
            }

            double alpha = 1.0 - cc;
            var u = Ucl.Compute(detected, cc, boot, seed);
            var sw = GoodnessOfFit.ShapiroWilk(detected);
            var lf = GoodnessOfFit.Lilliefors(detected);
            bool gammaPass = false, gammaAd = false, gammaKs = false;
            double adCrit = double.NaN, ksCrit = double.NaN;
            GammaGofResult? gg = null;
            if (u.GammaApplicable)
            {
                gg = GammaGof.Compute(detected);
                adCrit = gg.CriticalValue(GammaGofTest.AndersonDarling, SnapAlpha(alpha));
                ksCrit = gg.CriticalValue(GammaGofTest.KolmogorovSmirnov, SnapAlpha(alpha));
                gammaAd = gg.AD_A2 <= adCrit;
                gammaKs = gg.KS_D <= ksCrit;
                gammaPass = gammaAd || gammaKs;   // ProUCL 5.2: one of the two tests suffices
            }
            var normalConclusion = GoodnessOfFit.Conclude(sw, lf, alpha);
            bool normalPass = normalConclusion != GofConclusion.NotNormal;

            // ND-aware UCLs (computed up front so the recommendation can use them).
            UclWithNdResult? un = null;
            if (s.NondetectCount > 0 && detected.Length >= 2)
            {
                try { un = UclWithNd.Compute(s.Observations, cc, boot, seed); }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: ND UCLs: {ex.Message}");
                }
            }

            var sheet = new ReportSheet($"OpenUCL {Version} — UCL / EPC ESTIMATION ({cc * 100:G4}% confidence)");
            sheet.Header.Add(("input", $"{spec.File} (sha256 {fileHash})"));
            sheet.Header.Add(("series", $"{s.PointId} / {s.Parameter}    n = {s.N}    detects = {detected.Length}    ND = {s.NondetectCount}{(s.N > 0 ? $" ({100.0 * s.NondetectCount / s.N:G4}%)" : "")}"));
            sheet.Header.Add(("options", $"bootstrap runs = {boot}, seed = {seed}"));
            sheet.Header.Add(("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));

            var sum = sheet.AddSection("summary statistics");
            sum.ColumnsAre("statistic", "value");
            sum.AddRow("mean (detected)", F(u.Mean));
            sum.AddRow("sd (detected)", F(u.StandardDeviation));
            sum.AddRow("skewness k3-hat", F(u.Skewness));

            var gof = sheet.AddSection("goodness of fit (detected observations)");
            gof.ColumnsAre("test", "statistic", "critical / p", "conclusion");
            gof.AddRow("Shapiro-Wilk (AS R94)", $"W = {F(sw.W)}", $"p = {(sw.PValue < 1e-4 ? "<1e-4" : F(sw.PValue))}", sw.W >= sw.CriticalValue(alpha) ? "pass" : "fail");
            gof.AddRow("Lilliefors K-S", $"D = {F(lf.D)}", F(lf.CriticalValue(alpha)), lf.D <= lf.CriticalValue(alpha) ? "pass" : "fail");
            if (gg is not null)
            {
                gof.AddRow("Gamma A-D (SSI 2002)", $"A2 = {F(gg.AD_A2)}", F(adCrit), gammaAd ? "pass" : "fail");
                gof.AddRow("Gamma K-S (SSI 2002)", $"D = {F(gg.KS_D)}", F(ksCrit), gammaKs ? "pass" : "fail");
            }
            string gofText = gammaPass ? "detected data follow an approximate gamma distribution"
                : normalPass == false ? "detected data do not follow normal, lognormal or gamma distributions"
                : normalConclusion == GofConclusion.Normal ? "detected data follow an approximate normal distribution"
                : "detected data follow an approximate normal distribution (one of two tests)";
            gof.AddNote(gofText + $" at {alpha:G2} significance (ProUCL 5.2 logic: one of two tests).");

            if (gg is not null)
            {
                var gam = sheet.AddSection("gamma parameters (MLE on detected data)");
                gam.ColumnsAre("parameter", "value");
                gam.AddRow("k-hat (MLE)", F(u.KHat));
                gam.AddRow("k-star (bias corrected)", F(u.KStar));
                gam.AddRow("theta-hat (MLE)", F(u.ThetaHat));
                gam.AddRow("theta-star (bias corrected)", F(u.ThetaStar));
                gam.AddRow($"chi-square ({alpha:G2})", F(u.ChiSquareApprox));
                if (detected.Length <= 50)
                {
                    gam.AddRow($"adjusted significance beta ({alpha:G2})", F(u.BetaAdjusted));
                    gam.AddRow("adjusted chi-square (beta)", F(u.ChiSquareAdjusted));
                }
            }

            var ucls = sheet.AddSection($"ucls / epcs ({cc * 100:G4}% confidence)");
            ucls.ColumnsAre("method", "UCL", "reference");
            string? recommended = null; string rationale = "";
            if (un is not null && gammaPass && !double.IsNaN(un.GrosGammaAdjusted))
            {
                ucls.AddRowHighlighted("GROS gamma adjusted", F(un.GrosGammaAdjusted), "TG eq. 2-35 on GROS data (n<=50)");
                recommended = $"GROS gamma adjusted UCL = {F(un.GrosGammaAdjusted)}";
                rationale = $"nondetects present ({s.NondetectCount} of {s.N}); detected data approximate gamma " +
                            $"(one of A-D/K-S passes at {alpha:G2}); gamma UCLs computed on the GROS-imputed data set " +
                            "(TG example 4-4 logic).";
            }
            else if (un is not null && normalPass)
            {
                ucls.AddRowHighlighted("KM (t)", F(un.KmT), "KM mean + t * SE");
                recommended = $"KM (t) UCL = {F(un.KmT)}";
                rationale = "detected data are approximately normal; KM estimates in the t equation (TG section 4.6).";
            }
            else if (gammaPass && u.GammaApplicable && !double.IsNaN(u.GammaAdjusted))
            {
                ucls.AddRowHighlighted("gamma adjusted (n<=50)", F(u.GammaAdjusted), "TG eq. 2-35, table 2-2");
                recommended = $"gamma adjusted UCL = {F(u.GammaAdjusted)}";
                rationale = $"n<=50 and detected data approximate gamma (one of A-D/K-S passes at {alpha:G2}); k-star = {F(u.KStar)}.";
            }
            else if (gammaPass && u.GammaApplicable)
            {
                ucls.AddRowHighlighted("gamma approximate (n>50)", F(u.GammaApproximate), "TG eq. 2-34");
                recommended = $"gamma approximate UCL = {F(u.GammaApproximate)}";
                rationale = $"n>50 and detected data approximate gamma; k-star = {F(u.KStar)}.";
            }
            else if (normalPass)
            {
                ucls.AddRowHighlighted("Student's t", F(u.StudentT), "TG eq. 2-32");
                recommended = $"Student's t UCL = {F(u.StudentT)}";
                rationale = "detected data are approximately normal (ProUCL 5.2 logic).";
            }
            else if (Math.Abs(u.Skewness) <= 1.0 && un is null)
            {
                ucls.AddRowHighlighted("bootstrap-t", F(u.BootstrapT), "TG eq. 2-62");
                recommended = $"bootstrap-t UCL = {F(u.BootstrapT)}";
                rationale = "no parametric distribution identified; mild skewness favors the bootstrap-t.";
            }
            else if (un is not null)
            {
                ucls.AddRowHighlighted("KM percentile bootstrap", F(un.KmPercentileBootstrap), "bootstrap of observation pairs");
                recommended = $"KM percentile bootstrap UCL = {F(un.KmPercentileBootstrap)}";
                rationale = "no parametric distribution identified; KM bootstrap accounts for the nondetects " +
                            "(skewness caveat: coverage may fall short of nominal for highly skewed data).";
            }
            else if (detected.Length < 75)
            {
                recommended = null;
                rationale = "small sample with high skew and no identifiable distribution: " +
                            "no recommendation is provided (ProUCL 5.2 logic); consult a statistician.";
            }
            else
            {
                ucls.AddRowHighlighted("bootstrap-t", F(u.BootstrapT), "TG eq. 2-62");
                recommended = $"bootstrap-t UCL = {F(u.BootstrapT)}";
                rationale = "large sample: the bootstrap-t is the preferred nonparametric choice.";
            }

            ucls.AddRow("Student's t", F(u.StudentT), "TG eq. 2-32");
            ucls.AddRow("modified-t (Johnson)", F(u.ModifiedT), "TG eq. 2-37");
            ucls.AddRow("jackknife", F(u.Jackknife), "equals the t-UCL for a mean");
            ucls.AddRow("CLT", F(u.Clt), "TG eq. 2-40");
            ucls.AddRow("adjusted CLT (Chen)", F(u.AdjustedClt), "TG eq. 2-42");
            ucls.AddRow("Chebyshev (mean, sd)", F(u.ChebyshevMeanSd), "TG eq. 2-46; not recommended by ProUCL 5.2");
            if (gg is not null && !double.IsNaN(u.GammaApproximate))
            {
                ucls.AddRow("gamma approximate", F(u.GammaApproximate), "TG eq. 2-34");
                if (!double.IsNaN(u.GammaAdjusted))
                    ucls.AddRow("gamma adjusted", F(u.GammaAdjusted), "TG eq. 2-35");
            }
            ucls.AddRow("standard bootstrap", F(u.StandardBootstrap), $"TG eq. 2-56 (seed {seed})");
            ucls.AddRow("percentile bootstrap", F(u.PercentileBootstrap), "TG eq. 2-57");
            ucls.AddRow("BCA bootstrap", F(u.BcaBootstrap), "TG eq. 2-58..2-61");
            ucls.AddRow("bootstrap-t", F(u.BootstrapT), "TG eq. 2-62");

            var rec = sheet.AddSection("recommendation");
            rec.ColumnsAre("recommended UCL", "rationale");
            rec.AddRow(recommended ?? "none", rationale);

            if (un is not null)
            {
                var ndSec = sheet.AddSection("ucls accounting for nondetects");
                    ndSec.ColumnsAre("method", "UCL", "reference");
                    ndSec.AddRow("KM (t)", F(un.KmT), "KM mean + t * SE (TG eq. 4-1..4-2)");
                    ndSec.AddRow("KM (z)", F(un.KmZ), "KM mean + z * SE");
                    ndSec.AddRow("KM (Chebyshev)", F(un.KmChebyshev), "TG eq. 2-46 on KM estimates; not recommended by ProUCL 5.2");
                    ndSec.AddRow("KM percentile bootstrap", F(un.KmPercentileBootstrap), "bootstrap of observation pairs, seed " + seed);
                    ndSec.AddRow("KM BCA bootstrap", F(un.KmBcaBootstrap), "TG eq. 2-58..2-61 on KM means");
                    if (un.LrosT > 0)
                    {
                        ndSec.AddRow("LROS (t)", F(un.LrosT), "t-UCL on LROS-imputed data set");
                        ndSec.AddRow("LROS percentile bootstrap", F(un.LrosPercentileBootstrap), "percentile bootstrap on LROS data");
                    }
                    if (un.GrosT > 0)
                    {
                        ndSec.AddRow("GROS (t)", F(un.GrosT), "t-UCL on GROS-imputed data set");
                        if (!double.IsNaN(un.GrosGammaAdjusted))
                            ndSec.AddRow("GROS gamma adjusted", F(un.GrosGammaAdjusted), "TG eq. 2-35 on GROS data (n<=50)");
                        if (!double.IsNaN(un.GrosGammaApproximate))
                            ndSec.AddRow("GROS gamma approximate", F(un.GrosGammaApproximate), "TG eq. 2-34 on GROS data");
                    }
                    ndSec.AddNote("when detected data are gamma, ProUCL recommends gamma UCLs on the GROS data set (TG example 4-4); " +
                                  "when normal, the KM (t) UCL. DL/2-based UCLs are available but not recommended (TG section 4.2.3).");
            }

            Console.Write(sheet.RenderText());
            series++;
        }
        Console.WriteLine($"({series} series processed.)");
        return 0;
    }

    private static double SnapAlpha(double alpha) =>
        alpha <= 0.02 ? 0.01 : alpha <= 0.075 ? 0.05 : 0.10;

    private static string ShortHash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha.ComputeHash(stream);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < 4; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString() + "…";
    }

    private static int RunBtv(string[] args)
    {
        double cc = 0.95, coverage = 0.95;
        int k = 5;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--alpha" when i + 1 < args.Length:
                    cc = 1.0 - double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--coverage" when i + 1 < args.Length:
                    coverage = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--k" when i + 1 < args.Length:
                    k = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default:
                    rest.Add(args[i]); break;
            }
        }
        var spec = InputSpec.Parse(rest.ToArray());
        string fileHash = ShortHash(spec.File);

        int series = 0;
        foreach (var s in spec.ReadSeries())
        {
            double[] values = s.DetectedValues();
            if (values.Length < 3)
            {
                Console.Error.WriteLine($"warning: {s.PointId}/{s.Parameter}: fewer than 3 detects, BTVs skipped.");
                continue;
            }
            var b = Btv.Compute(values, cc, coverage, k);
            var sheet = new ReportSheet($"OpenUCL {Version} — BACKGROUND THRESHOLD VALUES ({cc * 100:G4}% confidence, {coverage * 100:G4}% coverage)");
            sheet.Header.Add(("input", $"{spec.File} (sha256 {fileHash})"));
            sheet.Header.Add(("series", $"{s.PointId} / {s.Parameter}    n = {s.N}    detects = {values.Length}    ND = {s.NondetectCount}"));
            sheet.Header.Add(("options", $"future k = {k} (Bonferroni UPLk)"));
            sheet.Header.Add(("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));

            var pct = sheet.AddSection("upper percentiles");
            pct.ColumnsAre("percentile", "value", "reference");
            pct.AddRow($"{coverage * 100:G4}% nonparametric", F(b.PercentileNonparametric), "TG eq. 3-1 (p*n, Excel-style)");
            pct.AddRow("normal", F(b.PercentileNormal), "TG eq. 3-2");
            if (b.LognormalApplicable) pct.AddRow("lognormal", F(b.PercentileLognormal), "TG eq. 3-3");
            if (b.GammaApplicable) pct.AddRow("gamma (MLE)", F(b.PercentileGamma), "TG eq. 3-5");

            var upl = sheet.AddSection("upper prediction limits");
            upl.ColumnsAre("limit", "value", "reference");
            upl.AddRow($"UPL1 normal", F(b.UplNormal1), "TG eq. 3-13");
            upl.AddRow($"UPL{b.FutureK} normal (Bonferroni)", F(b.UplNormalK), "TG eq. 3-18");
            upl.AddRow($"UPL{b.FutureK} mean of k, normal", F(b.UplNormalKMean), "t(1-a) * s * sqrt(1/k + 1/n)");
            if (b.LognormalApplicable) upl.AddRow("UPL1 lognormal", F(b.UplLognormal1), "TG eq. 3-14");
            if (b.GammaApplicable)
            {
                upl.AddRow("UPL1 gamma WH", F(b.UplGammaWh), "TG eq. 3-15 (cube-root scale)");
                upl.AddRow("UPL1 gamma HW", F(b.UplGammaHw), "TG eq. 3-16 (fourth-root scale)");
            }
            upl.AddRow("UPL1 nonparametric", F(b.UplNonparametric), "TG eq. 3-17");
            upl.AddRow("UPL1 Chebyshev", F(b.UplChebyshev), "TG section 3.5.4.1; conservative");

            var utl = sheet.AddSection("upper tolerance limits");
            utl.ColumnsAre("limit", "value", "reference");
            utl.AddRow("tolerance factor K", F(b.ToleranceFactorK), "exact noncentral-t (Hahn-Meeker tables)");
            utl.AddRow($"UTL{cc * 100:G4}-{coverage * 100:G4} normal", F(b.UtlNormal), "TG eq. 3-6");
            if (b.LognormalApplicable) utl.AddRow($"UTL{cc * 100:G4}-{coverage * 100:G4} lognormal", F(b.UtlLognormal), "TG eq. 3-7");
            if (b.GammaApplicable)
            {
                utl.AddRow($"UTL gamma WH", F(b.UtlGammaWh), "TG section 3.4.3 (cube-root scale)");
                utl.AddRow($"UTL gamma HW", F(b.UtlGammaHw), "TG section 3.4.3 (fourth-root scale)");
            }
            utl.AddRow($"UTL nonparametric (order r = {b.UtlNonparametricOrder})", F(b.UtlNonparametric),
                $"achieved CC = {F(b.UtlAchievedCC)} (TG eq. binomial coverage)");

            var usl = sheet.AddSection("upper simultaneous limits");
            usl.ColumnsAre("limit", "value", "reference");
            usl.AddRow("USL nonparametric (max)", F(b.UslNonparametric), "TG section 3.6; use only on outlier-free single populations");
            usl.AddNote("parametric USLs require the Singh (1993) d2max critical values and are not included yet.");

            Console.Write(sheet.RenderText());
            series++;
        }
        Console.WriteLine($"({series} series processed.)");
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
              ndstats <file>        estimates with nondetects: DL/2, Kaplan-Meier, LROS, GROS
              ucl <file>            UCL/EPC suite per series with recommendation sheet

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
              --alpha <x>           gof/ucl: significance level (default 0.05)
              --boot <n>            ucl: bootstrap runs (default 2000)
              --seed <n>            ucl: bootstrap seed (default 381, printed on sheets)
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
