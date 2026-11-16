using System.Data;
using System.Globalization;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenUCL.Core;
using OpenUCL.Data;

namespace OpenUCL.Gui;

public partial class MainWindow : Window
{
    private static readonly string Version = (
        System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0-dev").Split('+')[0];

    private List<SeriesRecord> _series = new();
    private string _filePath = "";

    public MainWindow() => InitializeComponent();

    private void Log(string message) => LogList.Items.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");

    private void ShowTab(string header)
    {
        foreach (var item in CenterTabs.Items)
            if (item is TabItem tab && tab.Header as string == header)
            {
                CenterTabs.SelectedItem = tab;
                break;
            }
    }

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
        _filePath = path;
        _series = new List<SeriesRecord>();
        SeriesList.Items.Clear();
        SheetPreview.Text = "Loading...";
        try
        {
            bool isSpreadsheet = Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm" or ".xls";
            bool asLong = isSpreadsheet || LongFormatCheck.IsChecked == true;
            var data = new DataTable("data");

            if (asLong)
            {
                var options = new LongTableReadOptions { ColumnSeparator = ';' };
                var table = LongTableReader.ReadTable(path, options);
                var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var valueColumns = table.Columns
                    .Where(c => !excluded.Contains(c) && IsNumericColumn(table, c)).ToList();
                _series = LongTableReader.ToSeries(table,
                    new SeriesSelection { ValueColumns = valueColumns }, options).ToList();

                // data grid = the loaded table, column by column
                foreach (string col in table.Columns) data.Columns.Add(col, typeof(string));
                for (int r = 0; r < table.RowCount; r++)
                {
                    var cells = new string[table.Columns.Count];
                    for (int c = 0; c < table.Columns.Count; c++)
                    {
                        var d = table.GetDouble(r, c);
                        cells[c] = d is not null
                            ? d.Value.ToString("G6", CultureInfo.InvariantCulture)
                            : table.GetString(r, c);
                    }
                    data.Rows.Add(cells);
                }
                FileInfoText.Text = $"{Path.GetFileName(path)} (long layout) — " +
                                    $"{table.RowCount} rows, {valueColumns.Count} value column(s)";
            }
            else
            {
                _series = PackedSeriesReader.ReadFile(path).ToList();

                // data grid = observation-level long form derived from the packed file
                data.Columns.Add("Point", typeof(string));
                data.Columns.Add("Parameter", typeof(string));
                data.Columns.Add("Value", typeof(string));
                foreach (var s in _series)
                    foreach (var o in s.Observations)
                        data.Rows.Add(s.PointId, s.Parameter,
                            o.IsNondetect ? "<" + o.Value.ToString("G6", CultureInfo.InvariantCulture)
                                          : o.Value.ToString("G6", CultureInfo.InvariantCulture));
                FileInfoText.Text = $"{Path.GetFileName(path)} (packed layout) — {_series.Count} series";
            }

            DataGrid.ItemsSource = data.DefaultView;
            ShowTab("Data");

            SeriesList.Items.Clear();
            foreach (var s in _series)
                SeriesList.Items.Add($"{Truncate(s.PointId, 14)}  {Truncate(s.Parameter, 12)}  N={s.N} ND={s.NondetectCount}");
            Log($"loaded {Path.GetFileName(path)}: {_series.Count} series.");
        }
        catch (Exception ex)
        {
            SheetPreview.Text = "";
            DataGrid.ItemsSource = null;
            Log($"ERROR loading {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private void OnSeriesSelected(object? sender, SelectionChangedEventArgs e)
    {
        int index = SeriesList.SelectedIndex;
        if (index < 0 || index >= _series.Count) return;
        ShowTab("Sheet");
        try
        {
            SheetPreview.Text = BuildSeriesSheet(_series[index]).RenderText();
        }
        catch (Exception ex)
        {
            SheetPreview.Text = $"error: {ex.Message}";
            Log($"ERROR on series {_series[index].PointId}: {ex.Message}");
        }
    }

    /// <summary>Summary sheet for one series: descriptive statistics on the
    /// detected values, plus Kaplan-Meier estimates when nondetects are
    /// present (TG eq. 4-1..4-3). Built with the same ReportSheet renderer
    /// used by the command-line sheets.</summary>
    private ReportSheet BuildSeriesSheet(SeriesRecord s)
    {
        var sheet = new ReportSheet($"OpenUCL {Version} — SERIES SUMMARY");
        sheet.Header.Add(("input", _filePath));
        sheet.Header.Add(("series", $"{s.PointId} / {s.Parameter}    n = {s.N}    ND = {s.NondetectCount}"));
        sheet.Header.Add(("generated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)));

        var stats = sheet.AddSection("general statistics (detected observations)");
        stats.ColumnsAre("statistic", "value");
        double[] detected = s.DetectedValues();
        if (detected.Length > 0)
        {
            var d = Descriptive.Compute(detected);
            stats.AddRow("n (detected)", d.N.ToString(CultureInfo.InvariantCulture));
            stats.AddRow("minimum", F(d.Min));
            stats.AddRow("maximum", F(d.Max));
            stats.AddRow("median", F(d.Median));
            stats.AddRow("mean", F(d.Mean));
            stats.AddRow("standard deviation", d.N > 1 ? F(d.StandardDeviation) : "n/a");
        }
        else
        {
            stats.AddRow("n (detected)", "0");
        }

        if (s.NondetectCount > 0)
        {
            try
            {
                var km = Censored.KaplanMeier(s.Observations);
                var kmSec = sheet.AddSection("kaplan-meier estimates (nondetects included)");
                kmSec.ColumnsAre("estimate", "value", "reference");
                kmSec.AddRow("KM mean", F(km.Mean), "TG eq. 4-1");
                kmSec.AddRow("KM standard error", F(km.StandardErrorOfMean), "TG eq. 4-2");
                kmSec.AddRow("KM sd", F(km.Sd), "TG eq. 4-3");
            }
            catch (ArgumentException ex)
            {
                sheet.AddSection("kaplan-meier estimates")
                    .AddNote($"not computable: {ex.Message}");
            }
        }

        sheet.AddSection("notes")
            .AddNote("full analysis modules (UCLs, BTVs, hypothesis tests) are added in upcoming releases.");
        return sheet;
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

    private static string F(double? v) => v is double d && !double.IsNaN(d)
        ? d.ToString("G6", CultureInfo.InvariantCulture) : "-";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "~";
}
