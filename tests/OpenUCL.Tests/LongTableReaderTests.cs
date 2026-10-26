using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

public class LongTableReaderTests
{
    private static string TestData(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Fact]
    public void Reads_long_csv_with_group_and_nd_flag()
    {
        var table = LongTableReader.ReadTable(TestData("long-sample.csv"));
        Assert.Equal(5, table.Columns.Count);
        Assert.Equal(9, table.RowCount);

        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { "TCE" }, GroupColumn = "Well" },
            new LongTableReadOptions { NdFlagColumn = "C" }).ToList();

        Assert.Equal(2, series.Count);
        var mw1 = series[0];
        Assert.Equal("MW-1", mw1.PointId);
        Assert.Equal("TCE", mw1.Parameter);
        Assert.Equal(5, mw1.N);
        Assert.Equal(1, mw1.NondetectCount);
        Assert.True(mw1.Observations[1].IsNondetect);
        Assert.Equal(0.5, mw1.Observations[1].Value, 12); // ProUCL convention: value column holds DL
    }

    [Fact]
    public void Detection_limit_column_overrides_value_column_for_nds()
    {
        var table = LongTableReader.ReadTable(TestData("long-sample.csv"));
        var options = new LongTableReadOptions { NdFlagColumn = "C", DetectionLimitColumn = "DL" };
        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { "TCE" }, GroupColumn = "Well" }, options).ToList();

        var mw1 = series[0];
        Assert.True(mw1.Observations[1].IsNondetect);
        Assert.Equal(0.25, mw1.Observations[1].Value, 12); // explicit DL column wins
    }

    [Fact]
    public void Multiple_value_columns_and_missing_cells()
    {
        var table = LongTableReader.ReadTable(TestData("long-sample.csv"));
        var series = LongTableReader.ToSeries(table,
            new SeriesSelection { ValueColumns = new[] { "TCE", "PCE" }, GroupColumn = "Well" },
            new LongTableReadOptions { NdFlagColumn = "C" }).ToList();

        Assert.Equal(4, series.Count);
        var pce = series.Single(s => s.Parameter == "PCE" && s.PointId == "MW-1");
        Assert.Equal(4, pce.N);       // one empty PCE cell is missing
        Assert.Equal(1, pce.SkippedTokens);
    }

    [Fact]
    public void Reads_xlsx_built_at_runtime()
    {
        string path = Path.Combine(Path.GetTempPath(), $"openucl-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            var rows = new List<dynamic>
            {
                new Dictionary<string, object> { ["Well"] = "MW-1", ["TCE"] = 1.2, ["C"] = "" },
                new Dictionary<string, object> { ["Well"] = "MW-1", ["TCE"] = 0.5, ["C"] = "ND" },
                new Dictionary<string, object> { ["Well"] = "MW-2", ["TCE"] = 2.5, ["C"] = "" },
            };
            MiniExcelLibs.MiniExcel.SaveAs(path, rows);

            var table = LongTableReader.ReadTable(path);
            Assert.Equal(new[] { "Well", "TCE", "C" }, table.Columns.ToArray());
            var series = LongTableReader.ToSeries(table,
                new SeriesSelection { ValueColumns = new[] { "TCE" }, GroupColumn = "Well" },
                new LongTableReadOptions { NdFlagColumn = "C" }).ToList();

            Assert.Equal(2, series.Count);
            Assert.Equal(2, series[0].N);
            Assert.True(series[0].Observations[1].IsNondetect);
            Assert.Equal(0.5, series[0].Observations[1].Value, 12);
            Assert.Equal("MW-2", series[1].PointId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_column_throws_with_available_names()
    {
        var table = LongTableReader.ReadTable(TestData("long-sample.csv"));
        var ex = Assert.Throws<FormatException>(() =>
            LongTableReader.ToSeries(table,
                new SeriesSelection { ValueColumns = new[] { "NOT_THERE" } },
                new LongTableReadOptions()).ToList());
        Assert.Contains("NOT_THERE", ex.Message);
        Assert.Contains("Well", ex.Message);
    }
}
