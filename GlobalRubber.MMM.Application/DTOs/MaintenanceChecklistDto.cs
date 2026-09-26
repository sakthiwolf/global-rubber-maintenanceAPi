namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Maintenance checklist returned to API callers (masters.maintenance_checklist_master plus its items).</summary>
public sealed class MaintenanceChecklistDto
{
    public int ChecklistId { get; init; }
    public string ChecklistCode { get; init; } = string.Empty;
    public string ChecklistName { get; init; } = string.Empty;

    /// <summary>Machine / Mold.</summary>
    public string AppliesTo { get; init; } = string.Empty;

    /// <summary>Daily / Weekly / Monthly / Yearly; null only on an inactive legacy checklist.</summary>
    public string? Frequency { get; init; }

    /// <summary>The machine a Machine checklist belongs to (null for Mold, and on an inactive legacy checklist).</summary>
    public int? MachineId { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }

    /// <summary>False when the assigned machine has been deactivated since (the UI shows it as "(not active)").</summary>
    public bool? MachineIsActive { get; init; }

    /// <summary>The cycle anchor / first due date (yyyy-MM-dd); null only on inactive legacy checklists and optional for Mold.</summary>
    public DateOnly? StartDate { get; init; }

    public bool IsActive { get; init; }

    /// <summary>The checklist's items in sort order (masters.maintenance_checklist_item_master).</summary>
    public IReadOnlyList<MaintenanceChecklistItemDto> Items { get; init; } = Array.Empty<MaintenanceChecklistItemDto>();

    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version of the header. The client keeps it and sends it back on PUT: if the checklist (header or items)
    /// changed in between, the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>One checklist item as stored.</summary>
public sealed class MaintenanceChecklistItemDto
{
    public int ChecklistItemId { get; init; }
    public int SortOrder { get; init; }
    public string ItemLabel { get; init; } = string.Empty;
}
