namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// Replacement status in the Mold Reports, DERIVED from the mold master (there is no replacement transaction - user
/// decision 2026-09-28: nothing is ever reported as "replaced"):
///   Retired              - status Retired
///   Replacement Required - not Retired, and life_state Replace (usage &gt;= replacement shots) or status Replacement Due
/// </summary>
public static class MoldReplacementStatus
{
    public const string ReplacementRequired = "Replacement Required";
    public const string Retired = "Retired";

    public static readonly IReadOnlyList<string> All = new[] { ReplacementRequired, Retired };
}

/// <summary>Sort keys of the Mold Reports.</summary>
public static class MoldReportSort
{
    public const string Code = "code";
    public const string Usage = "usage";
    public const string RangeShots = "rangeShots";
    public const string LifeUsed = "lifeUsed";
    public const string Remaining = "remaining";

    /// <summary>Usage tab.</summary>
    public static readonly IReadOnlyList<string> UsageSorts = new[] { Code, Usage, RangeShots, LifeUsed };

    /// <summary>Life Status and Replacement tabs.</summary>
    public static readonly IReadOnlyList<string> LifeSorts = new[] { LifeUsed, Remaining, Code };
}
