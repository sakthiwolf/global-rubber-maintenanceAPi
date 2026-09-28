using System.Globalization;
using System.Text;

namespace GlobalRubber.MMM.Application.Common;

/// <summary>A generated file returned by an export endpoint.</summary>
public sealed record ReportFile(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Builds an RFC 4180 CSV (CRLF line endings, UTF-8 with BOM so Excel opens it correctly) from typed rows and
/// human-readable column headers. Values are formatted with the invariant culture (the server's culture is en-IN,
/// which would write "1,00,000"). Cells that a spreadsheet would treat as a formula (starting with = + - @ tab or CR)
/// are prefixed with an apostrophe (CSV/formula injection guard); a plain number such as -10000 (shots past a PM
/// threshold) is left as it is, so it stays a number.
/// </summary>
public sealed class CsvReportWriter<T>
{
    public const string ContentType = "text/csv";

    private readonly List<(string Header, Func<T, string?> Value)> _columns = new();

    public CsvReportWriter<T> Column(string header, Func<T, string?> value)
    {
        _columns.Add((header, value));
        return this;
    }

    public byte[] Write(IEnumerable<T> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", _columns.Select(c => Escape(c.Header)))).Append("\r\n");

        foreach (var row in rows)
        {
            builder.Append(string.Join(",", _columns.Select(c => Escape(c.Value(row))))).Append("\r\n");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return preamble.Concat(body).ToArray();
    }

    // ------------------------------------------------------------ value formatting shared by the report exports

    public static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string? Time(TimeOnly? value) => value?.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string? DateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string? Number(decimal? value) => value?.ToString("0.0#", CultureInfo.InvariantCulture);

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool IsPlainNumber(string value) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if ((value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') && !IsPlainNumber(value))
            value = "'" + value;

        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
