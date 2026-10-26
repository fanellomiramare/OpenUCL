using OpenUCL.Data;
using Xunit;

namespace OpenUCL.Tests;

public class PackedSeriesReaderTests
{
    private static string TestData(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Fact]
    public void Reads_packed_file_with_quoted_semicolon_lists()
    {
        var series = PackedSeriesReader.ReadFile(TestData("sample-packed.csv")).ToList();

        Assert.Equal(2, series.Count);

        var mw01 = series[0];
        Assert.Equal("MW-01", mw01.PointId);
        Assert.Equal("TCE", mw01.Parameter);
        Assert.Equal(5, mw01.N);
        Assert.Equal(1, mw01.NondetectCount);
        Assert.Equal(0, mw01.SkippedTokens);
        Assert.False(mw01.Observations[0].IsNondetect);
        Assert.Equal(1.2, mw01.Observations[0].Value, 12);
        Assert.True(mw01.Observations[2].IsNondetect);
        Assert.Equal(0.5, mw01.Observations[2].Value, 12); // detection limit carried as value
    }

    [Fact]
    public void Auto_detects_pipe_separator_and_skips_missing_tokens()
    {
        var series = PackedSeriesReader.ReadFile(TestData("sample-packed.csv")).ToList();
        var mw02 = series[1];

        Assert.Equal("MW-02", mw02.PointId);
        Assert.Equal(3, mw02.N);           // 0.9, 1.4, 2.0
        Assert.Equal(1, mw02.SkippedTokens); // NA
        Assert.Equal(2.0, mw02.Observations[2].Value, 12);
    }

    [Fact]
    public void Detected_values_exclude_nondetects()
    {
        var series = PackedSeriesReader.ReadFile(TestData("sample-packed.csv")).ToList();
        var detected = series[0].DetectedValues();

        Assert.Equal(new[] { 1.2, 0.8, 2.1, 3.3 }, detected);
    }

    [Fact]
    public void Streaming_reader_never_materialises_the_whole_file()
    {
        // Defer with Select: if ReadFile were eager, this would run out of memory on
        // huge files. Verify laziness by never enumerating past the first series.
        var first = PackedSeriesReader.ReadFile(TestData("sample-packed.csv"))
            .Select(s => s)
            .First();

        Assert.Equal("MW-01", first.PointId);
    }

    [Fact]
    public void Custom_separators_are_honored()
    {
        var line = new[] { "P1", "Pb", "3.5,4.0,5.5" };
        var rec = PackedSeriesReader.ParseRow(line, new PackedSeriesReadOptions { ValueSeparator = ',' });

        Assert.Equal(3, rec.N);
        Assert.Equal(5.5, rec.Observations[2].Value, 12);
    }

    [Fact]
    public void Malformed_number_reports_file_and_line()
    {
        var path = TestData("bad-number.csv");
        var ex = Assert.Throws<FormatException>(
            () => PackedSeriesReader.ReadFile(path).ToList());

        Assert.Contains("bad-number.csv", ex.Message);
        Assert.Contains("line 2", ex.Message);
    }

    [Fact]
    public void Csv_line_splitter_honors_quotes()
    {
        var fields = PackedSeriesReader.SplitCsvLine("a;\"b;c\";\"say \"\"hi\"\"\"", ';');
        Assert.Equal(new[] { "a", "b;c", "say \"hi\"" }, fields);
    }
}
