using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One PM occurrence in the Maintenance Reports - a Machine PM (transactions.machine_pm_transaction) or a Mold PM
/// (transactions.mold_pm_transaction). Fields that one workflow does not have are null: Machine PMs have a plan
/// (checklist), frequency, legacy maintenance type and checklist progress; Mold PMs have a category and shot figures.
/// </summary>
public sealed class MaintenanceReportItemDto
{
    /// <summary>Machine PM / Mold PM.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Machine / Mold.</summary>
    public string AssetType { get; init; } = string.Empty;

    /// <summary>machine_pm_id or mold_pm_id (unique only together with Source).</summary>
    public int RecordId { get; init; }
    public string PmNo { get; init; } = string.Empty;
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;

    /// <summary>Mold PM only (e.g. Shot-based).</summary>
    public string? Category { get; init; }

    /// <summary>Machine PM only: the maintenance plan (checklist) and its current frequency.</summary>
    public string? PlanCode { get; init; }
    public string? PlanName { get; init; }
    public string? Frequency { get; init; }

    /// <summary>Machine PM only, historical PMs (recurring occurrences have none).</summary>
    public string? MaintenanceTypeName { get; init; }

    /// <summary>Due date (Mold PM: the IST date the PM became due).</summary>
    public DateOnly ScheduledDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary>Scheduled / Due / Overdue / In Progress / Completed - each workflow's own rule (MaintenanceReportStatus).</summary>
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }

    /// <summary>Overdue PMs only: whole days since the due date (IST today - due date).</summary>
    public int? DaysOverdue { get; init; }

    /// <summary>Who performed the maintenance (free text entered at completion).</summary>
    public string? PerformedBy { get; init; }

    /// <summary>Mold PM only: the mold's current cumulative usage, the PM's threshold/interval and usage figures.</summary>
    public int? CurrentUsageShots { get; init; }
    public int? ThresholdShots { get; init; }
    public int? IntervalShots { get; init; }
    public int? UsageAtTrigger { get; init; }
    public int? UsageAtCompletion { get; init; }

    /// <summary>Open Mold PM with a threshold: threshold - current usage (negative = shots past the threshold), as the Mold PM page.</summary>
    public int? RemainingShots { get; init; }

    /// <summary>Machine PM only: checklist snapshot progress.</summary>
    public int? ChecklistItemsDone { get; init; }
    public int? ChecklistItemsTotal { get; init; }

    public string? Remarks { get; init; }
}

/// <summary>Overdue counts over the WHOLE filtered set.</summary>
public sealed class MaintenanceOverdueSummaryDto
{
    public int TotalOverdue { get; init; }
    public int MachinePmOverdue { get; init; }
    public int MoldPmOverdue { get; init; }
}

/// <summary>Body of GET .../overdue: the summary plus one page of rows.</summary>
public sealed class MaintenanceOverdueReportDto
{
    public MaintenanceOverdueSummaryDto Summary { get; init; } = new();
    public PagedResult<MaintenanceReportItemDto> Page { get; init; } = PagedResult<MaintenanceReportItemDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>Filter options for every Maintenance Reports tab, under RPT_MAINTENANCE View.</summary>
public sealed class MaintenanceReportLookupsDto
{
    public IReadOnlyList<ReportLookupItemDto> Machines { get; init; } = Array.Empty<ReportLookupItemDto>();
    public IReadOnlyList<ReportLookupItemDto> Molds { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Machine maintenance plans (checklists that apply to machines).</summary>
    public IReadOnlyList<ReportLookupItemDto> MaintenancePlans { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Maintenance types that apply to machines (Machine / Both).</summary>
    public IReadOnlyList<ReportLookupItemDto> MaintenanceTypes { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Mold PM categories (CK values).</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AssetTypes { get; init; } = Array.Empty<string>();

    /// <summary>Statuses of open PMs (PM Schedule filter).</summary>
    public IReadOnlyList<string> OpenStatuses { get; init; } = Array.Empty<string>();

    /// <summary>Every status (Maintenance History filter).</summary>
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();
}
