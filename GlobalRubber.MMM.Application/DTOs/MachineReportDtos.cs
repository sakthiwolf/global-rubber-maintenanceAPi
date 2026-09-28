using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One row of the Machine List report (masters.machine_master + department). Last/next maintenance are the machine's
/// system-managed dates (set by Machine PM completion - MachinePmRules), never recalculated by the report.
/// </summary>
public sealed class MachineListReportItemDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Criticality { get; init; } = string.Empty;
    public string OperationalStatus { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateOnly? LastMaintenanceDate { get; init; }
    public DateOnly? NextMaintenanceDate { get; init; }
}

/// <summary>One Machine PM occurrence (transactions.machine_pm_transaction) in the Maintenance History report.</summary>
public sealed class MachineMaintenanceHistoryReportItemDto
{
    public int MachinePmId { get; init; }
    public string PmNo { get; init; } = string.Empty;
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;

    /// <summary>The maintenance plan (checklist) the occurrence belongs to and its current frequency.</summary>
    public string? ChecklistCode { get; init; }
    public string? ChecklistName { get; init; }
    public string? Frequency { get; init; }

    /// <summary>Historical PMs only - recurring occurrences have no maintenance type (migration 012).</summary>
    public string? MaintenanceTypeName { get; init; }

    public DateOnly ScheduledDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary>Completed / Scheduled / In Progress as stored, or Overdue when not completed and due before today (IST).</summary>
    public string Status { get; init; } = string.Empty;
    public bool IsOverdue { get; init; }

    /// <summary>Who performed the maintenance (free text entered at completion).</summary>
    public string? MaintenanceBy { get; init; }

    public int ChecklistItemsDone { get; init; }
    public int ChecklistItemsTotal { get; init; }
    public string? Remarks { get; init; }
}

/// <summary>
/// One breakdown (transactions.machine_breakdown_transaction) in the Breakdown and Downtime reports. Timestamps are
/// converted from the stored UTC to plant (IST) local time. DowntimeHours is the value the breakdown workflow stored
/// when it reached Resolved (resolved - maintenance started); it is null while the breakdown is open.
/// </summary>
public sealed class MachineBreakdownReportItemDto
{
    public int MachineBreakdownId { get; init; }
    public string BreakdownNo { get; init; } = string.Empty;
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;

    /// <summary>The machine's department.</summary>
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>When the breakdown happened, as entered (plant local date and time).</summary>
    public DateOnly BreakdownDate { get; init; }
    public TimeOnly BreakdownTime { get; init; }

    public string Problem { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? BreakdownTypeName { get; init; }
    public string Priority { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;

    /// <summary>Open / Resolved (MachineReportBreakdownStatus), derived from the stage.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Free text, exactly as stored.</summary>
    public string? ReportedBy { get; init; }
    public string? AssignedEngineerName { get; init; }

    public DateTime? AssignedAt { get; init; }
    public DateTime? MaintenanceStartedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public DateTime? ClosedAt { get; init; }

    public string? RootCause { get; init; }
    public string? CorrectiveAction { get; init; }
    public decimal? DowntimeHours { get; init; }
}

/// <summary>
/// Report-level downtime figures over the whole filtered set (not just the page), calculated in the database. Only
/// resolved breakdowns with a stored downtime count towards total/average/longest; open ones are counted separately.
/// </summary>
public sealed class MachineDowntimeSummaryDto
{
    public int BreakdownCount { get; init; }
    public int OpenCount { get; init; }
    public int ResolvedCount { get; init; }

    /// <summary>Resolved breakdowns that have a stored downtime (the ones the figures below are calculated from).</summary>
    public int DowntimeRecordCount { get; init; }

    public decimal TotalDowntimeHours { get; init; }

    /// <summary>Total / DowntimeRecordCount rounded to 2 decimals; null when there is no downtime record.</summary>
    public decimal? AverageDowntimeHours { get; init; }

    public decimal? LongestDowntimeHours { get; init; }
    public string? LongestDowntimeBreakdownNo { get; init; }
}

/// <summary>Body of GET /api/v1/reports/machines/downtime: the summary plus one page of rows.</summary>
public sealed class MachineDowntimeReportDto
{
    public MachineDowntimeSummaryDto Summary { get; init; } = new();
    public PagedResult<MachineBreakdownReportItemDto> Page { get; init; } = PagedResult<MachineBreakdownReportItemDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>A filter option from a master table (inactive ones are kept - reports cover history).</summary>
public sealed class ReportLookupItemDto
{
    public int Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
}

/// <summary>
/// Filter options for every Machine Reports tab (GET /api/v1/reports/machines/lookups), served under the report's View
/// permission so a report user does not also need master permissions. Fixed value lists mirror the database CHECKs.
/// </summary>
public sealed class MachineReportLookupsDto
{
    public IReadOnlyList<ReportLookupItemDto> Machines { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Departments that have at least one machine.</summary>
    public IReadOnlyList<ReportLookupItemDto> Departments { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Distinct machine types in use.</summary>
    public IReadOnlyList<string> MachineTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ReportLookupItemDto> BreakdownTypes { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Maintenance types that apply to machines (Machine / Both).</summary>
    public IReadOnlyList<ReportLookupItemDto> MaintenanceTypes { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Machine maintenance plans (checklists that apply to machines).</summary>
    public IReadOnlyList<ReportLookupItemDto> MaintenancePlans { get; init; } = Array.Empty<ReportLookupItemDto>();

    public IReadOnlyList<string> Criticalities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> OperationalStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Priorities { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Stages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MaintenanceStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BreakdownStatuses { get; init; } = Array.Empty<string>();
}
