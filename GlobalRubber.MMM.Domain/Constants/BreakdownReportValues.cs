namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>The dimensions the Breakdown Analysis report groups by.</summary>
public static class BreakdownReportGroupBy
{
    public const string Machine = "machine";
    public const string Type = "type";
    public const string Department = "department";
    public const string Priority = "priority";
    public const string Stage = "stage";
    public const string Status = "status";

    public static readonly IReadOnlyList<string> All = new[] { Machine, Type, Department, Priority, Stage, Status };
}

/// <summary>Sort keys of the Breakdown Analysis groups (default: count, largest first).</summary>
public static class BreakdownReportGroupSort
{
    public const string Count = "count";
    public const string Downtime = "downtime";
    public const string Name = "name";

    public static readonly IReadOnlyList<string> All = new[] { Count, Downtime, Name };
}

/// <summary>Labels the Breakdown Reports use.</summary>
public static class BreakdownReportLabels
{
    /// <summary>Breakdowns without a breakdown type - the template's own label (analysis R-17).</summary>
    public const string UnspecifiedType = "Unspecified";
}
