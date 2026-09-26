namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/machine-maintenance/{id}/complete: who performed the maintenance, the ticks for the PM's
/// own snapshot lines, the remarks, and the row version the caller last read. Unchecked items are allowed (Q-17); lines
/// not listed are saved unchecked. The completion date is today (server clock, plant time zone) - never client-controlled.
/// </summary>
public sealed class CompleteMachinePmRequest
{
    /// <summary>Required: the person who actually performed the maintenance - free text, at most 100 characters, trimmed.</summary>
    public string? MaintenanceBy { get; init; }

    public IReadOnlyList<MachinePmChecklistResultRequest>? Results { get; init; }

    /// <summary>Optional, at most 1000 characters (NVARCHAR(1000)).</summary>
    public string? Remarks { get; init; }

    /// <summary>Base64 row_version exactly as returned by MachinePmDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>One tick: a snapshot line of THIS PM (MachinePmChecklistItemDto.MachinePmChecklistId) and whether it was done.</summary>
public sealed class MachinePmChecklistResultRequest
{
    public int MachinePmChecklistId { get; init; }
    public bool IsChecked { get; init; }
}
