using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/reports/molds (and .../export). Molds have no is_active column: "active" means status is not
/// Retired (the Mold master's own rule).
/// </summary>
public sealed class MoldListReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on mold code, name, type, location or product code/name.</summary>
    public string? Search { get; set; }

    public int? MoldId { get; set; }
    public int? ProductId { get; set; }

    /// <summary>Exact mold type (values from GET .../lookups).</summary>
    public string? MoldType { get; set; }

    /// <summary>Available / In Production / Maintenance / Replacement Due / Retired (MoldStatus).</summary>
    public string? Status { get; set; }

    /// <summary>Normal / Warning / Replace (MoldLifeState - the persisted computed column).</summary>
    public string? LifeState { get; set; }

    /// <summary>true = not Retired, false = Retired, omitted = both.</summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/molds/usage (and .../export). Range figures are the Saved production entries of the
/// mold in the range (1 piece = 1 shot, as Production Entry adds them - user decision 2026-09-28). When a date range or a
/// machine is given, only molds with such production are listed; without them every mold is listed with its all-time
/// production.
/// </summary>
public sealed class MoldUsageReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on mold code, name or product code/name.</summary>
    public string? Search { get; set; }

    public int? MoldId { get; set; }

    /// <summary>Production on this machine only.</summary>
    public int? MachineId { get; set; }

    /// <summary>Production entries on or after this date.</summary>
    public DateOnly? FromDate { get; set; }

    /// <summary>Production entries on or before this date.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>Normal / Warning / Replace.</summary>
    public string? LifeState { get; set; }

    /// <summary>code (default, asc) / usage / rangeShots / lifeUsed (MoldReportSort.UsageSorts).</summary>
    public string? SortBy { get; set; }

    /// <summary>asc / desc.</summary>
    public string? SortDirection { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/molds/life-status and .../replacement (and their exports). The replacement report
/// is limited to molds that need replacement or are Retired (MoldReplacementStatus).
/// </summary>
public sealed class MoldLifeReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on mold code, name, type or product code/name.</summary>
    public string? Search { get; set; }

    public int? MoldId { get; set; }
    public int? ProductId { get; set; }

    /// <summary>Normal / Warning / Replace.</summary>
    public string? LifeState { get; set; }

    /// <summary>Mold status (MoldStatus).</summary>
    public string? Status { get; set; }

    /// <summary>Replacement report only: Replacement Required / Retired.</summary>
    public string? ReplacementStatus { get; set; }

    /// <summary>lifeUsed (default, desc) / remaining / code (MoldReportSort.LifeSorts).</summary>
    public string? SortBy { get; set; }

    /// <summary>asc / desc.</summary>
    public string? SortDirection { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/molds/maintenance (and .../export). The date range applies to the maintenance date:
/// the completed date of a completed PM, otherwise the date it became due.
/// </summary>
public sealed class MoldMaintenanceReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on PM number, mold code/name, maintenance by or remarks.</summary>
    public string? Search { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int? MoldId { get; set; }

    /// <summary>PM category (CK_mold_pm_transaction_category values; the automatic PM is Shot-based).</summary>
    public string? Category { get; set; }

    /// <summary>due / overdue / in-progress / completed - the Mold PM page's own buckets (MoldPmBucket).</summary>
    public string? Status { get; set; }
}
