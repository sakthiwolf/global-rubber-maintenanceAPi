namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Filter validation shared by the report services: fixed values are matched case-insensitively and replaced by their
/// stored spelling (so SQL compares exact values); every problem is collected and thrown as one ValidationException (400).
/// </summary>
internal static class ReportFilterValidation
{
    /// <summary>Null for empty; the allowed spelling for a case-insensitive match; otherwise an error.</summary>
    public static string? Canonical(string? value, IReadOnlyList<string> allowed, string name, List<string> errors)
    {
        var trimmed = Trimmed(value);
        if (trimmed is null)
            return null;

        var match = allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            errors.Add($"{name} must be one of: {string.Join(", ", allowed)}.");

        return match;
    }

    public static void CheckRange(DateOnly? from, DateOnly? to, string fromName, string toName, List<string> errors)
    {
        if (from is { } f && to is { } t && f > t)
            errors.Add($"{fromName} must be on or before {toName}.");
    }

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
