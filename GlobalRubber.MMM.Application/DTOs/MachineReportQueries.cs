using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of GET /api/v1/reports/machines (and .../export): paging (export ignores it) plus the Machine List filters.
/// Every filter is optional; values are validated by MachineReportService (400 for an unknown status/criticality or an
/// inverted date range).
/// </summary>
public sealed class MachineListReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on machine code, name, type, location or department name.</summary>
    public string? Search { get; set; }

    public int? MachineId { get; set; }
    public int? DepartmentId { get; set; }

    /// <summary>Exact machine type (the values come from GET .../lookups).</summary>
    public string? MachineType { get; set; }

    /// <summary>Running / Idle / Breakdown / Maintenance (MachineOperationalStatus).</summary>
    public string? OperationalStatus { get; set; }

    /// <summary>Low / Medium / High (MachineCriticality).</summary>
    public string? Criticality { get; set; }

    /// <summary>true = active machines only, false = inactive only, omitted = both.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Machines whose next maintenance date is on or after this date.</summary>
    public DateOnly? NextMaintenanceFrom { get; set; }

    /// <summary>Machines whose next maintenance date is on or before this date.</summary>
    public DateOnly? NextMaintenanceTo { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/machines/maintenance-history (and .../export). The date range applies to the
/// maintenance date: the completed date of a completed PM, otherwise its scheduled (due) date.
/// </summary>
public sealed class MachineMaintenanceHistoryReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on PM number, machine code/name, plan code/name, performed by or remarks.</summary>
    public string? Search { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int? MachineId { get; set; }
    public int? MaintenanceTypeId { get; set; }

    /// <summary>The maintenance plan (masters.maintenance_checklist_master) the occurrence belongs to.</summary>
    public int? ChecklistId { get; set; }

    /// <summary>Completed / Scheduled / Overdue (MachineReportPmStatus); omitted = every occurrence.</summary>
    public string? Status { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/machines/breakdowns and .../downtime (and their exports) - and of the Breakdown
/// Reports (/api/v1/reports/breakdowns), which share these filters. The date range applies to the breakdown date.
/// </summary>
public class MachineBreakdownReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on breakdown number, machine code/name, problem, reported by, engineer or breakdown type.</summary>
    public string? Search { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int? MachineId { get; set; }
    public int? BreakdownTypeId { get; set; }

    /// <summary>The machine's department.</summary>
    public int? DepartmentId { get; set; }

    /// <summary>Case-insensitive "contains" match on Reported By (free text, as stored).</summary>
    public string? ReportedBy { get; set; }

    /// <summary>Low / Medium / High / Critical (BreakdownPriority).</summary>
    public string? Priority { get; set; }

    /// <summary>Reported / Assigned / Maintenance Started / Resolved / Closed (BreakdownStage).</summary>
    public string? Stage { get; set; }

    /// <summary>Open / Resolved (MachineReportBreakdownStatus).</summary>
    public string? Status { get; set; }

    /// <summary>date (default) / machine / duration / priority / status (MachineReportBreakdownSort).</summary>
    public string? SortBy { get; set; }

    /// <summary>asc / desc (default desc).</summary>
    public string? SortDirection { get; set; }
}
