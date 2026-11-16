using System.Data;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenUCL.Core;
using OpenUCL.Data;
using OpenUCL.Reports;

namespace OpenUCL.Gui;

public partial class MainWindow : Window
{
    private static readonly string Version = (
        System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0-dev").Split('+')[0];

    /// <summary>An open document: the loaded data file (grid + series) or a
    /// generated analysis output (sheet preview). Modelled on the ProUCL
    /// workflow: each module run creates a new named document.</summary>
    private sealed class Doc
    {
        public required string Name { get; set; }
        public required string Kind { get; init; }        // "data" | "outlier"
        public string FilePath = "";                      // data docs: the source file
        public bool IsPacked { get; init; }
        public List<SeriesRecord> Series = new();
        public DataTable Grid = new();
        public LongTable? Table;                          // long-layout source (for reloads)
        public LongTableReadOptions? TableOptions;
        public string SheetText = "";                     // output docs: preview text
        public Dictionary<string, string> Summary = new();
        public string? SavedPath;                         // set after the first Save As
        public string? SavedFormat;
    }

    private readonly List<Doc> _docs = new();
    private bool _syncing;
    private bool _fillingFlagBox;

    public MainWindow() => InitializeComponent();

    private void Log(string message) => LogList.Items.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");

    // ---- document model ----------------------------------------------------

    private Doc? Active =>
        CenterTabs.SelectedIndex >= 0 && CenterTabs.SelectedIndex < _docs.Count
            ? _docs[CenterTabs.SelectedIndex] : null;

    private void SyncDocuments(int select)
    {
        _syncing = true;
        CenterTabs.Items.Clear();
        SeriesList.Items.Clear();
        for (int i = 0; i < _docs.Count; i++)
        {
            var d = _docs[i];
            string header = d.Name + (d.Kind == "data" ? "" : "  [output]");
            var tabItem = new TabItem { Header = header };
            if (d.Kind == "data")
            {
                d.Grid ??= BuildGridDataTable(d);
                var grid = new DataGrid
                {
                    AutoGenerateColumns = true,
                    IsReadOnly = true,
                    CanUserSortColumns = true,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    ItemsSource = d.Grid.DefaultView,
                };
                tabItem.Content = grid;
            }
            else
            {
                tabItem.Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        FontFamily = new Avalonia.Media.FontFamily("Consolas, 'Courier New', monospace"),
                        FontSize = 13,
                        Margin = new Avalonia.Thickness(8, 6),
                        TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                        Text = d.SheetText,
                    }
                };
            }
            CenterTabs.Items.Add(tabItem);
            SeriesList.Items.Add($"{Truncate(d.Name, 24)}  [{d.Kind}]");
        }
        CenterTabs.SelectedIndex = select;
        SeriesList.SelectedIndex = select;
        _syncing = false;
    }

    private DataTable BuildGridDataTable(Doc doc)
    {
        var dt = new DataTable("data");
        if (doc.Table is not null)   // long layout: the loaded table, column by column
        {
            foreach (string col in doc.Table.Columns) dt.Columns.Add(col, typeof(string));
            for (int r = 0; r < doc.Table.RowCount; r++)
            {
                var cells = new string[doc.Table.Columns.Count];
                for (int c = 0; c < doc.Table.Columns.Count; c++)
                {
                    var d = doc.Table.GetDouble(r, c);
                    cells[c] = d is not null
                        ? d.Value.ToString("G6", CultureInfo.InvariantCulture)
                        : doc.Table.GetString(r, c);
                }
                dt.Rows.Add(cells);
            }
        }
        else                          // packed layout: observation-level long form
        {
            dt.Columns.Add("Point", typeof(string));
            dt.Columns.Add("Parameter", typeof(string));
            dt.Columns.Add("Value", typeof(string));
            foreach (var s in doc.Series)
                foreach (var o in s.Observations)
                    dt.Rows.Add(s.PointId, s.Parameter,
                        o.IsNondetect ? "<" + o.Value.ToString("G6", CultureInfo.InvariantCulture)
                                      : o.Value.ToString("G6", CultureInfo.InvariantCulture));
        }
        return dt;
    }

    private void CenterTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SeriesList is null || CenterTabs is null) return;
        int i = CenterTabs.SelectedIndex;
        if (i >= 0 && i < _docs.Count && SeriesList.SelectedIndex != i)
            SeriesList.SelectedIndex = i;
    }

    private void SeriesList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || CenterTabs is null) return;
        int i = SeriesList.SelectedIndex;
        if (i >= 0 && i < _docs.Count && CenterTabs.SelectedIndex != i)
            CenterTabs.SelectedIndex = i;
    }

    private void Activate(int index) => SyncDocuments(select: index);

    // ---- File: open / close -------------------------------------------------

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Open data file",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Data files") { Patterns = new[] { "*.csv", "*.txt", "*.xls", "*.xlsx" } },
                FilePickerFileTypes.All,
            },
        };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) return;
        LoadFile(files[0].Path.LocalPath);
    }

    private void LoadFile(string path)
    {
        try
        {
            bool isSpreadsheet = Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls";
            bool asLong = isSpreadsheet || LongFormatCheck.IsChecked == true;

            var doc = new Doc
            {
                Name = Path.GetFileName(path),
                Kind = "data",
                FilePath = path,
                IsPacked = !asLong,
            };

            if (asLong)
            {
                var options = new LongTableReadOptions { ColumnSeparator = ';' };
                doc.Table = LongTableReader.ReadTable(path, options);
                doc.TableOptions = options;
                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var valueColumns = doc.Table.Columns
                    .Where(c => !excluded.Contains(c) && IsNumericColumn(doc.Table, c)).ToList();
                doc.Series = LongTableReader.ToSeries(doc.Table,
                    new SeriesSelection { ValueColumns = valueColumns }, options).ToList();
                FileInfoText.Text = $"{Path.GetFileName(path)} (long layout) — " +
                                    $"{doc.Table.RowCount} rows, {valueColumns.Count} value column(s)";
            }
            else
            {
                doc.Series = PackedSeriesReader.ReadFile(path).ToList();
                FileInfoText.Text = $"{Path.GetFileName(path)} (packed layout) — {doc.Series.Count} series";
            }

            doc.Grid = BuildGridDataTable(doc);
            AddDoc(doc);
            Activate(_docs.Count - 1);
            Log($"loaded {Path.GetFileName(path)}: {doc.Series.Count} series.");
        }
        catch (Exception ex)
        {
            Log($"ERROR loading {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private void AddDoc(Doc doc)
    {
        _docs.Add(doc);
        SyncDocuments(select: _docs.Count - 1);
    }

    /// <summary>Long layout: rebuilds the series list and the data grid using
    /// the ND flag column currently selected (explicit designation — the
    /// ProUCL d_/D_ auto-detection is intentionally NOT replicated).</summary>
    private void ApplyLongSeries(Doc doc)
    {
        if (doc.Table is null || doc.TableOptions is null) return;

        string? flagCol = FlagColumnBox.SelectedItem as string;
        bool hasFlag = flagCol is not null && flagCol != "(none)";
        string ndValue = FlagValueBox.Text?.Trim() ?? "0";

        var options = new LongTableReadOptions
        {
            ColumnSeparator = doc.TableOptions.ColumnSeparator,
            HasHeader = doc.TableOptions.HasHeader,
            NdFlagColumn = hasFlag ? flagCol : null,
            NdFlagValue = hasFlag ? ndValue : "ND",
            DetectionLimitColumn = doc.TableOptions.DetectionLimitColumn,
        };

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hasFlag) excluded.Add(flagCol!);
        var valueColumns = doc.Table.Columns
            .Where(c => !excluded.Contains(c) && IsNumericColumn(doc.Table, c)).ToList();
        doc.Series = LongTableReader.ToSeries(doc.Table,
            new SeriesSelection { ValueColumns = valueColumns }, options).ToList();

        // data grid: the loaded table column by column; when a flag column is
        // designated, value cells of ND rows get the "<" prefix (ProUCL-like)
        doc.Grid = new DataTable("data");
        foreach (string col in doc.Table.Columns) doc.Grid.Columns.Add(col, typeof(string));
        int flagIdx = hasFlag ? doc.Table.ColumnIndex(flagCol!) : -1;
        for (int r = 0; r < doc.Table.RowCount; r++)
        {
            var cells = new string[doc.Table.Columns.Count];
            bool isNd = false;
            for (int c = 0; c < doc.Table.Columns.Count; c++)
            {
                string colName = doc.Table.Columns[c];
                var d = doc.Table.GetDouble(r, c);
                string cell = d is not null
                    ? d.Value.ToString("G6", CultureInfo.InvariantCulture)
                    : doc.Table.GetString(r, c);
                if (c == flagIdx)
                    isNd = d is not null && d.Value.ToString(CultureInfo.InvariantCulture) == ndValue;
                if (hasFlag && isNd && valueColumns.Contains(colName) && !cell.StartsWith('<'))
                    cell = "<" + cell;
                cells[c] = cell;
            }
            doc.Grid.Rows.Add(cells);
        }

        FileInfoText.Text = $"{Path.GetFileName(doc.FilePath)} (long layout) — " +
                            $"{doc.Table.RowCount} rows, {valueColumns.Count} value column(s)";
    }

    private void FillFlagColumnBox(IEnumerable<string> columns)
    {
        _fillingFlagBox = true;
        FlagColumnBox.Items.Clear();
        FlagColumnBox.Items.Add("(none)");
        foreach (string c in columns) FlagColumnBox.Items.Add(c);
        FlagColumnBox.SelectedIndex = 0;
        _fillingFlagBox = false;
    }

    private void OnFlagColumnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_fillingFlagBox) return;
        var doc = Active;
        if (doc is not null && doc.Kind == "data" && doc.Table is not null)
        {
            ApplyLongSeries(doc);
            SyncDocuments(select: CenterTabs.SelectedIndex);
        }
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

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        int i = CenterTabs.SelectedIndex;
        if (i < 0 || i >= _docs.Count) return;
        var doc = _docs[i];
        _docs.RemoveAt(i);
        SyncDocuments(select: Math.Min(i, _docs.Count - 1));
        Log($"{doc.Name} closed.");
    }

    // ---- Statistical Tests: Outlier pilot -----------------------------------

    private void OnOutlierComputeClick(object? sender, RoutedEventArgs e)
    {
        var doc = Active;
        if (doc is null || doc.Kind != "data")
        {
            Log("outlier tests: select a data document first.");
            return;
        }
        if (doc.IsPacked)
        {
            Log("outlier tests on packed (multi-series) files run in batch mode — coming in an upcoming release.");
            return;
        }

        double alpha = 0.05;
        int r0 = Outliers.RosnerMaxOutliers;
        var outputText = new System.Text.StringBuilder();
        var warnings = new List<string>();
        int k = 0;
        foreach (var s in doc.Series)
        {
            double[] detected = s.DetectedValues();
            if (detected.Length < 3) continue;
            var (sheet, _, buildWarns) = OutlierSheets.Build(
                detected, alpha, r0, Version, s.PointId, s.Parameter, s.NondetectCount);
            warnings.AddRange(buildWarns.Select(w => $"{s.PointId}/{s.Parameter}: {w}"));
            if (k > 0) outputText.AppendLine();
            outputText.Append(sheet.RenderText());
            k++;
        }
        if (k == 0) { Log("outlier tests: no series with at least 3 detected values."); return; }
        foreach (var w in warnings) Console.Error.WriteLine($"warning: {w}");

        string name = Path.GetFileNameWithoutExtension(doc.FilePath) + "_Outlier";
        var outDoc = new Doc
        {
            Name = name,
            Kind = "outlier",
            FilePath = doc.FilePath,
            SheetText = outputText.ToString(),
        };
        AddDoc(outDoc);
        Activate(_docs.Count - 1);
        Log($"generated outlier results ({k} series processed).");
        foreach (var w in warnings) Log($"warning: {w}");
    }

    // ---- Save / Save As ------------------------------------------------------

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var doc = Active;
        if (doc is null) { Log("nothing to save."); return; }
        if (doc.SavedPath is null || doc.SavedFormat is null) { OnSaveAsClick(sender, e); return; }
        WriteDocument(doc, doc.SavedPath, doc.SavedFormat);
        Log($"saved {doc.Name} → {doc.SavedPath}");
    }

    private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
    {
        var doc = Active;
        if (doc is null) { Log("nothing to save."); return; }

        bool isData = doc.Kind == "data";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save {doc.Name}",
            SuggestedFileName = Path.GetFileNameWithoutExtension(
                string.IsNullOrEmpty(doc.SavedPath) ? doc.Name : doc.SavedPath),
            DefaultExtension = isData ? "xlsx" : "txt",
            FileTypeChoices = isData
                ? new List<FilePickerFileType>
                  {
                      new("Excel workbook (.xlsx)") { Patterns = new[] { "*.xlsx" } },
                      new("CSV (.csv)") { Patterns = new[] { "*.csv" } },
                  }
                : new List<FilePickerFileType>
                  {
                      new("Text sheet (.txt)") { Patterns = new[] { "*.txt" } },
                  },
        });
        if (file is null) return;
        string path = file.Path.LocalPath;
        string format = path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ? "xlsx"
            : path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? "csv" : "txt";
        WriteDocument(doc, path, format);
        doc.SavedPath = path;
        doc.SavedFormat = format;
        Log($"saved {doc.Name} → {path} ({format})");
    }

    private void WriteDocument(Doc doc, string path, string format)
    {
        if (doc.Kind == "outlier")
        {
            File.WriteAllText(path, doc.SheetText, System.Text.Encoding.UTF8);
            return;
        }

        // data document: CSV or XLSX from the grid content
        if (format == "csv")
        {
            var header = string.Join(",", doc.Grid.Columns.Cast<DataColumn>()
                .Select(c => EscapeCsv(c.ColumnName)));
            var body = new List<string>();
            foreach (DataRow row in doc.Grid.Rows)
            {
                var cells = new string[doc.Grid.Columns.Count];
                for (int c = 0; c < doc.Grid.Columns.Count; c++)
                    cells[c] = EscapeCsv(row[c]?.ToString() ?? "");
                body.Add(string.Join(",", cells));
            }
            File.WriteAllLines(path, new[] { header }.Concat(body), System.Text.Encoding.UTF8);
            return;
        }

        // xlsx via MiniExcel: dictionary rows (sheet 'data')
        var columns = doc.Grid.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var rows = new List<Dictionary<string, object>>();
        foreach (DataRow row in doc.Grid.Rows)
        {
            var dict = new Dictionary<string, object>();
            for (int c = 0; c < doc.Grid.Columns.Count; c++)
            {
                var v = row[c];
                dict[columns[c]] = v is double d ? d
                    : double.TryParse(v?.ToString(), CultureInfo.InvariantCulture, out var dd) ? dd
                    : v?.ToString() ?? "";
            }
            rows.Add(dict);
        }
        MiniExcelLibs.MiniExcel.SaveAs(path, new Dictionary<string, object> { ["data"] = rows });
    }

    private static string EscapeCsv(string cell) =>
        cell.Contains(',') || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r')
            ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;


    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "~";
}
