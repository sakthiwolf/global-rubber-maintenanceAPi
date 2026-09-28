using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of every Maintenance Reports tab (GET /api/v1/reports/maintenance/schedule | completed | overdue | history
/// and their exports). A filter that only exists on one PM workflow leaves the other workflow's rows out when it is set
/// (e.g. a Category - Mold PM only - returns no Machine PMs; a Maintenance Plan - Machine PM only - returns no Mold PMs).
///
/// The date range applies to the tab's own date: schedule / overdue = due (scheduled) date; completed = completed date;
/// history = maintenance date (completed date, else due date).
/// </summary>
public sealed class MaintenanceReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on PM number, asset code/name, plan code/name, performed by or remarks.</summary>
    public string? Search { get; set; }

    /// <summary>Machine / Mold (MaintenanceReportAssetType).</summary>
    public string? AssetType { get; set; }

    public int? MachineId { get; set; }
    public int? MoldId { get; set; }

    /// <summary>Mold PM category (CK_mold_pm_transaction_category).</summary>
    public string? Category { get; set; }

    /// <summary>Machine PM maintenance plan (masters.maintenance_checklist_master).</summary>
    public int? ChecklistId { get; set; }

    /// <summary>Machine PM maintenance type (historical PMs only - recurring occurrences have none).</summary>
    public int? MaintenanceTypeId { get; set; }

    /// <summary>
    /// Schedule: Scheduled / Due / Overdue / In Progress. History: those or Completed. Not accepted on Completed / Overdue
    /// (their status is fixed).
    /// </summary>
    public string? Status { get; set; }

    /// <summary>Case-insensitive "contains" match on who performed the maintenance (free text entered at completion).</summary>
    public string? PerformedBy { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>date / asset / source / status (MaintenanceReportSort). Default: date.</summary>
    public string? SortBy { get; set; }

    /// <summary>asc / desc. Default: schedule and overdue earliest first; completed and history newest first.</summary>
    public string? SortDirection { get; set; }
}
