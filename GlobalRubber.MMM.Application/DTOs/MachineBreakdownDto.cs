namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// A machine breakdown occurrence (transactions.machine_breakdown_transaction) with its machine
/// and breakdown type joined for display.
/// </summary>
public sealed class MachineBreakdownDto
{
    public int MachineBreakdownId { get; init; }
    public string BreakdownNo { get; init; } = string.Empty;

    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;

    public DateOnly BreakdownDate { get; init; }
    public TimeOnly BreakdownTime { get; init; }

    /// <summary>Free text - exactly as typed, NOT an employee id or code.</summary>
    public string? ReportedBy { get; init; }

    public string Problem { get; init; } = string.Empty;

    public int? BreakdownTypeId { get; init; }
    public string? BreakdownTypeName { get; init; }

    /// <summary>Low / Medium / High / Critical.</summary>
    public string Priority { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Reported / Assigned / Maintenance Started / Resolved / Closed.</summary>
    public string Stage { get; init; } = string.Empty;

    public int? AssignedEngineerId { get; init; }
    public string? AssignedEngineerName { get; init; }
    public DateTime? AssignedAt { get; init; }
    public DateTime? MaintenanceStartedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public string? RootCause { get; init; }
    public string? CorrectiveAction { get; init; }
    public decimal? DowntimeHours { get; init; }

    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}
