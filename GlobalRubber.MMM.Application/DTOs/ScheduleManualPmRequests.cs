namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Body of POST /api/v1/machine-maintenance/manual - Manual / One-Time preventive maintenance (migration 025): exactly ONE
/// machine PM on one date, never recurring. There is no frequency (a frequency sent by a client is not bound - ignored).
/// </summary>
public sealed class ScheduleManualMachinePmRequest
{
    /// <summary>Required: the reason / name of the maintenance, trimmed, at most 150 characters.</summary>
    public string? Title { get; init; }

    /// <summary>Required: an existing, active machine.</summary>
    public int? MachineId { get; init; }

    /// <summary>Required: the business date (yyyy-MM-dd) the maintenance is due - stored exactly as sent.</summary>
    public DateOnly? MaintenanceDate { get; init; }

    /// <summary>Optional, as on a plan: an existing, active maintenance type that applies to Machine or Both.</summary>
    public int? MaintenanceTypeId { get; init; }

    /// <summary>Optional: an active Machine Checklist Master with items - its items are copied into the PM (snapshot).</summary>
    public int? SourceChecklistId { get; init; }
}

/// <summary>
/// Body of POST /api/v1/mold-maintenance/manual - Manual / One-Time mold PM (migration 025): ONE PM (category 'Scheduled')
/// on one date, independent of the mold's usage-based cycle. Mold PMs have no maintenance type (Q-15).
/// </summary>
public sealed class ScheduleManualMoldPmRequest
{
    /// <summary>Required: the reason / name of the maintenance, trimmed, at most 150 characters.</summary>
    public string? Title { get; init; }

    /// <summary>Required: an existing mold that is not Retired.</summary>
    public int? MoldId { get; init; }

    /// <summary>Required: the business date (yyyy-MM-dd) the maintenance is due - stored exactly as sent.</summary>
    public DateOnly? MaintenanceDate { get; init; }

    /// <summary>Optional: an active Mold Checklist Master with items - its items are copied into the PM (snapshot).</summary>
    public int? SourceChecklistId { get; init; }
}
