using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One row of the Mold List report. Life figures follow the Mold master: LifeState is the persisted computed column,
/// the PM figures follow MoldPmRules (the same values the Mold PM page shows). There is no machine on a mold.
/// </summary>
public sealed class MoldListReportItemDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string MoldType { get; init; } = string.Empty;
    public int CavityCount { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? Location { get; init; }
    public int CurrentUsageShots { get; init; }
    public int MaximumShots { get; init; }
    public int WarningShots { get; init; }
    public int ReplacementShots { get; init; }
    public string LifeState { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    /// <summary>Not Retired.</summary>
    public bool IsActive { get; init; }

    /// <summary>The last completed shot-based PM (date and the usage it was completed at).</summary>
    public DateOnly? LastMaintenanceDate { get; init; }
    public int? LastMaintenanceUsage { get; init; }

    /// <summary>PM interval; null = usage-based PM not configured.</summary>
    public int? PmIntervalShots { get; init; }

    /// <summary>The usage at which the next PM falls due (cycle start + interval) - PM is shot-based, not date-based.</summary>
    public long? PmNextThresholdShots { get; init; }
    public long? PmRemainingShots { get; init; }

    /// <summary>Not Configured / Normal / Warning / Due / Overdue / In Maintenance (MoldPmRules.StateOf).</summary>
    public string PmState { get; init; } = string.Empty;
}

/// <summary>One mold in the Usage report: the authoritative current usage plus the production in the selected range.</summary>
public sealed class MoldUsageReportItemDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int CurrentUsageShots { get; init; }
    public int MaximumShots { get; init; }
    public int ReplacementShots { get; init; }

    /// <summary>max(0, maximum - current) - the Mold master's rule.</summary>
    public int RemainingShots { get; init; }

    /// <summary>current / maximum, rounded, capped at 100 - the Mold master's rule (MoldService.LifeUsedPercent).</summary>
    public int LifeUsedPercent { get; init; }
    public string LifeState { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    /// <summary>SUM(production_qty) of the Saved production entries in the range (and on the machine, when filtered).</summary>
    public long RangeShots { get; init; }
    public int RangeEntryCount { get; init; }

    /// <summary>The latest of those entries.</summary>
    public DateOnly? LastProductionDate { get; init; }
    public string? LastMachineCode { get; init; }
    public string? LastMachineName { get; init; }
}

/// <summary>One mold in the Life Status and Replacement reports.</summary>
public sealed class MoldLifeReportItemDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string MoldType { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int CurrentUsageShots { get; init; }
    public int MaximumShots { get; init; }
    public int WarningShots { get; init; }
    public int ReplacementShots { get; init; }
    public int RemainingShots { get; init; }
    public int LifeUsedPercent { get; init; }
    public string LifeState { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;

    /// <summary>Replacement Required / Retired (MoldReplacementStatus); null when neither applies.</summary>
    public string? ReplacementStatus { get; init; }
}

/// <summary>Counts over the WHOLE filtered set (one SQL aggregate), not just the page.</summary>
public sealed class MoldLifeSummaryDto
{
    public int TotalMolds { get; init; }
    public int NormalCount { get; init; }
    public int WarningCount { get; init; }

    /// <summary>life_state Replace (usage &gt;= replacement shots).</summary>
    public int ReplaceCount { get; init; }

    /// <summary>Not Retired and (life_state Replace or status Replacement Due).</summary>
    public int ReplacementRequiredCount { get; init; }
    public int RetiredCount { get; init; }
}

/// <summary>Body of GET .../life-status and .../replacement: the summary plus one page of rows.</summary>
public sealed class MoldLifeReportDto
{
    public MoldLifeSummaryDto Summary { get; init; } = new();
    public PagedResult<MoldLifeReportItemDto> Page { get; init; } = PagedResult<MoldLifeReportItemDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>One Mold PM (transactions.mold_pm_transaction) in the Maintenance report.</summary>
public sealed class MoldMaintenanceReportItemDto
{
    public int MoldPmId { get; init; }
    public string PmNo { get; init; } = string.Empty;
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;

    /// <summary>For the automatic PM: the IST date it became due.</summary>
    public DateOnly ScheduledDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary>Due / Overdue / In Progress / Completed - Scheduled is reported as Due or Overdue exactly as the Mold PM page.</summary>
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }

    public int? ThresholdShots { get; init; }
    public int? IntervalShots { get; init; }

    /// <summary>The mold's usage when the PM was created (mold_usage_at_service).</summary>
    public int UsageAtTrigger { get; init; }
    public int? UsageAtCompletion { get; init; }
    public string? MaintenanceBy { get; init; }
    public string? Remarks { get; init; }
}

/// <summary>Filter options for every Mold Reports tab, under RPT_MOLD View. Fixed lists mirror the database CHECKs.</summary>
public sealed class MoldReportLookupsDto
{
    public IReadOnlyList<ReportLookupItemDto> Molds { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Products that have at least one mold.</summary>
    public IReadOnlyList<ReportLookupItemDto> Products { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Machines that appear in production entries (the only mold-machine link).</summary>
    public IReadOnlyList<ReportLookupItemDto> Machines { get; init; } = Array.Empty<ReportLookupItemDto>();
    public IReadOnlyList<string> MoldTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LifeStates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MaintenanceStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReplacementStatuses { get; init; } = Array.Empty<string>();
}
