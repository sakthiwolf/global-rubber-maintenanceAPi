using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.machine_breakdown_transaction.
/// Stage progresses forward only: Reported → Assigned → Maintenance Started → Resolved → Closed.
/// ReportedBy is a free-text field (NVARCHAR(100)) - never an employee FK.
/// </summary>
public class MachineBreakdown : AuditableEntity
{
    public int MachineBreakdownId { get; set; }
    public string BreakdownNo { get; set; } = string.Empty;

    public int MachineId { get; set; }
    public DateOnly BreakdownDate { get; set; }
    public TimeOnly BreakdownTime { get; set; }

    /// <summary>Free-text name of the person who reported the breakdown. NOT an employee FK.</summary>
    public string? ReportedBy { get; set; }

    public string Problem { get; set; } = string.Empty;

    public int? BreakdownTypeId { get; set; }

    /// <summary>CK: Low / Medium / High / Critical. Default Medium.</summary>
    public string Priority { get; set; } = BreakdownPriority.Medium;

    public string? Description { get; set; }

    /// <summary>CK: Reported / Assigned / Maintenance Started / Resolved / Closed.</summary>
    public string Stage { get; set; } = BreakdownStage.Reported;

    public int? AssignedEngineerId { get; set; }
    public DateTime? AssignedAt { get; set; }
    public DateTime? MaintenanceStartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public decimal? DowntimeHours { get; set; }

    // Navigation properties
    public Machine Machine { get; set; } = null!;
    public BreakdownType? BreakdownType { get; set; }
    public Employee? AssignedEngineer { get; set; }
}
