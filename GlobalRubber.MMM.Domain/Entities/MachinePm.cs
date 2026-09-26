using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.machine_pm_transaction (a machine preventive-maintenance record). References Machine and
/// MaintenanceType (optional since migration 012 - recurring occurrences have none; historical rows keep theirs),
/// Employee as the engineer (optional; legacy rows only) and the checklist the snapshot was copied from
/// (optional) - all FK NO ACTION. Its checklist items are a snapshot in transactions.machine_pm_checklist_transaction
/// (CASCADE), so later edits to the checklist master never change a PM's history (analysis 6.3, 13.x).
/// </summary>
public class MachinePm : AuditableEntity
{
    public int MachinePmId { get; set; }
    public string PmNo { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public int? MaintenanceTypeId { get; set; }
    public DateOnly ScheduledDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public int? EngineerId { get; set; }
    public int? ChecklistId { get; set; }
    public string? Remarks { get; set; }

    /// <summary>Who actually performed the maintenance (free text, entered at completion - not an employee reference).</summary>
    public string? MaintenanceBy { get; set; }
    public string Status { get; set; } = MachinePmStatus.Scheduled;

    public Machine Machine { get; set; } = null!;
    public MaintenanceType? MaintenanceType { get; set; }
    public Employee? Engineer { get; set; }
    public MaintenanceChecklist? Checklist { get; set; }
    public List<MachinePmChecklistItem> ChecklistItems { get; set; } = new();
}
