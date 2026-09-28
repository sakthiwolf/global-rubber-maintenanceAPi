using System.Text;
using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Tests;

public class CsvReportWriterTests
{
    private sealed record Row(string? Text, decimal? Hours, DateOnly? Date);

    private static string Write(params Row[] rows)
    {
        var bytes = new CsvReportWriter<Row>()
            .Column("Text", r => r.Text)
            .Column("Hours (hrs)", r => CsvReportWriter<Row>.Number(r.Hours))
            .Column("Date", r => CsvReportWriter<Row>.Date(r.Date))
            .Write(rows);

        Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
    }

    [Fact]
    public void Empty_WritesTheHeaderOnly_WithCrlf()
    {
        Assert.Equal("Text,Hours (hrs),Date\r\n", Write());
    }

    [Fact]
    public void Quotes_Commas_Quotes_AndNewlines()
    {
        var csv = Write(new Row("a, b", null, null), new Row("say \"hi\"", null, null), new Row("line1\nline2", null, null));

        Assert.Contains("\"a, b\",,", csv);
        Assert.Contains("\"say \"\"hi\"\"\",,", csv);
        Assert.Contains("\"line1\nline2\",,", csv);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1+cmd|' /C calc'!A0", "'+1+cmd|' /C calc'!A0")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("-10000", "-10000")]
    [InlineData("-2.5", "-2.5")]
    [InlineData("normal", "normal")]
    public void FormulaLikeCells_ArePrefixed(string value, string expected)
    {
        Assert.StartsWith(expected + ",", Write(new Row(value, null, null)).Split("\r\n")[1]);
    }

    [Fact]
    public void NumbersAndDates_UseTheInvariantCulture()
    {
        var line = Write(new Row("x", 1234.5m, new DateOnly(2026, 9, 26))).Split("\r\n")[1];

        Assert.Equal("x,1234.5,2026-09-26", line);
    }

    [Fact]
    public void WholeHours_KeepOneDecimal()
    {
        Assert.Equal("x,6.0,", Write(new Row("x", 6m, null)).Split("\r\n")[1]);
    }
}
