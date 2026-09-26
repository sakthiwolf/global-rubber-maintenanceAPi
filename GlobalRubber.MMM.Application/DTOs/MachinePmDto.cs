namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// A machine preventive-maintenance occurrence (transactions.machine_pm_transaction) with its machine and the checklist it
/// belongs to joined for display, and its checklist snapshot in order.
/// </summary>
public sealed class MachinePmDto
{
    public int MachinePmId { get; init; }
    public string PmNo { get; init; } = string.Empty;

    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;

    /// <summary>The checklist the occurrence belongs to (its name and CURRENT frequency).</summary>
    public int? ChecklistId { get; init; }
    public string? ChecklistCode { get; init; }
    public string? ChecklistName { get; init; }
    public string? Frequency { get; init; }

    /// <summary>Historical PMs only (the recurring workflow has no maintenance type - migration 012).</summary>
    public int? MaintenanceTypeId { get; init; }
    public string? MaintenanceTypeName { get; init; }

    /// <summary>The occurrence's due date.</summary>
    public DateOnly ScheduledDate { get; init; }
    public DateOnly? CompletedDate { get; init; }

    /// <summary>Who actually performed the maintenance (free text, entered at completion).</summary>
    public string? MaintenanceBy { get; init; }

    public string? Remarks { get; init; }

    /// <summary>Scheduled / Completed ('In Progress' exists in the database but is never set - there is no start step).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Not completed and due before today's plant (IST) date - shown as a badge; never stored (D-03).</summary>
    public bool IsOverdue { get; init; }

    /// <summary>The checklist snapshot, in sort order.</summary>
    public IReadOnlyList<MachinePmChecklistItemDto> ChecklistItems { get; init; } = Array.Empty<MachinePmChecklistItemDto>();

    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>Base64 row_version. Required on completion: 409 if the PM changed since it was read.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>One snapshot checklist line.</summary>
public sealed class MachinePmChecklistItemDto
{
    public int MachinePmChecklistId { get; init; }
    public int SortOrder { get; init; }
    public string ItemLabel { get; init; } = string.Empty;
    public bool IsChecked { get; init; }
}

/// <summary>Number of PMs in each tab (GET /api/v1/machine-maintenance/counts), under the same machine/search filters.</summary>
public sealed class MachinePmBucketCountsDto
{
    /// <summary>Due now: not completed, Daily checklist, scheduled on or before today (overdue ones included).</summary>
    public int Daily { get; init; }
    public int Weekly { get; init; }
    public int Monthly { get; init; }
    public int Yearly { get; init; }

    /// <summary>Completed, any frequency.</summary>
    public int Completed { get; init; }
}
