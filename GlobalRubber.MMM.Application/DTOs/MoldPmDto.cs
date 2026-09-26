using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>A Mold PM as returned by GET /api/v1/mold-maintenance.</summary>
public sealed class MoldPmDto
{
    public int MoldPmId { get; init; }
    public string PmNo { get; init; } = string.Empty;
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string MoldStatus { get; init; } = string.Empty;

    /// <summary>'Shot-based' for the automatic PM.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>What created it: "Usage threshold" for the automatic usage-based PM.</summary>
    public string Trigger { get; init; } = string.Empty;

    /// <summary>For the automatic PM: the IST date it became due (the triggered date).</summary>
    public DateOnly ScheduledDate { get; init; }

    public DateOnly? CompletedDate { get; init; }

    /// <summary>The mold's cumulative usage when the PM was created.</summary>
    public int UsageAtTrigger { get; init; }

    /// <summary>The cycle threshold that made it due, and the interval at that moment.</summary>
    public int? ThresholdShots { get; init; }
    public int? IntervalShots { get; init; }

    /// <summary>The mold's cumulative usage now.</summary>
    public int CurrentShots { get; init; }

    /// <summary>Open PM: threshold - current shots (0 or negative = shots past the threshold). Null when completed.</summary>
    public long? RemainingShots { get; init; }

    /// <summary>The mold's usage when the PM was completed ("usage at last maintenance").</summary>
    public int? UsageAtCompletion { get; init; }

    /// <summary>Scheduled (not started) and it became due before today's IST date.</summary>
    public bool IsOverdue { get; init; }

    public string? MaintenanceBy { get; init; }
    public string? Remarks { get; init; }
    public string Status { get; init; } = string.Empty;

    /// <summary>True for a PM the system created automatically (created_by NULL).</summary>
    public bool CreatedBySystem { get; init; }

    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>Opaque base64 concurrency token - echo it back on start / complete.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>GET /api/v1/mold-maintenance query: paging (10 per page), the tab, one mold, a search on PM number / mold.</summary>
public sealed class MoldPmListQuery : PaginationRequest
{
    /// <summary>due / overdue / in-progress / completed (MoldPmBucket); omitted = every PM. Any other value is a 400.</summary>
    public string? Bucket { get; set; }

    public int? MoldId { get; set; }

    /// <summary>Case-insensitive "contains" match on the PM number, mold code or mold name.</summary>
    public string? Search { get; set; }
}

/// <summary>GET /api/v1/mold-maintenance/counts - one count per tab.</summary>
public sealed class MoldPmCountsDto
{
    public int Due { get; init; }
    public int Overdue { get; init; }
    public int InProgress { get; init; }
    public int Completed { get; init; }
}

/// <summary>
/// One mold's usage-based PM position (GET /api/v1/mold-maintenance/mold-usage). Everything is derived from the
/// authoritative columns (cumulative usage, interval, cycle start, warning margin) and the mold's open PM - nothing stored.
/// </summary>
public sealed class MoldUsageDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public string MoldStatus { get; init; } = string.Empty;

    /// <summary>Cumulative shots (the authoritative counter).</summary>
    public int CurrentShots { get; init; }

    /// <summary>The PM interval; null = usage-based PM not configured.</summary>
    public int? IntervalShots { get; init; }

    /// <summary>The warning margin in remaining shots; null = no warning.</summary>
    public int? WarningShots { get; init; }

    public int CycleStartShots { get; init; }
    public long? NextThresholdShots { get; init; }
    public long? RemainingShots { get; init; }
    public long? UsageSinceCycleStart { get; init; }

    /// <summary>The usage and date of the last completed automatic PM, if any.</summary>
    public int? LastMaintenanceShots { get; init; }
    public DateOnly? LastMaintenanceDate { get; init; }

    /// <summary>Not Configured / Normal / Warning / Due / Overdue / In Maintenance (MoldPmState).</summary>
    public string State { get; init; } = string.Empty;

    public int? OpenPmId { get; init; }
    public string? OpenPmNo { get; init; }
    public string? OpenPmStatus { get; init; }
    public DateOnly? OpenPmScheduledDate { get; init; }
}

/// <summary>The raw values the repository reads for one mold's PM position (the service derives the state).</summary>
public sealed record MoldUsageSnapshot(
    int MoldId, string MoldCode, string MoldName, string MoldStatus, int CurrentShots, int? IntervalShots, int? WarningShots,
    int CycleStartShots, int? LastMaintenanceShots, DateOnly? LastMaintenanceDate,
    int? OpenPmId, string? OpenPmNo, string? OpenPmStatus, DateOnly? OpenPmScheduledDate);

/// <summary>Body of PUT /api/v1/mold-maintenance/{id}/complete. The completion date is the server's (IST) "today".</summary>
public sealed class CompleteMoldPmRequest
{
    /// <summary>Required, free text, at most 100 characters (trimmed) - as Machine PM.</summary>
    public string? MaintenanceBy { get; init; }

    /// <summary>Optional, at most 1000 characters (remarks NVARCHAR(1000)).</summary>
    public string? Remarks { get; init; }

    public string? RowVersion { get; init; }
}

/// <summary>Body of PUT /api/v1/mold-maintenance/{id}/start - only the concurrency token.</summary>
public sealed class StartMoldPmRequest
{
    public string? RowVersion { get; init; }
}
