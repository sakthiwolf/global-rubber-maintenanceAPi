using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.mold_pm_transaction. The automatic, usage-based Mold PM (migration 014) is Category 'Shot-based':
/// created by the system (CreatedBy NULL) when the mold's cumulative usage reaches its cycle threshold
/// (<see cref="ThresholdShots"/> = cycle start + interval). ScheduledDate is the plant (IST) date it BECAME DUE - never a
/// future calendar date. At most one open Shot-based PM per mold (UX_mold_pm_transaction_open_shot_based) and one per
/// cycle threshold (UX_mold_pm_transaction_shot_based_threshold). The checklist snapshot / engineer / usage-reset columns
/// exist in the table but are not used by the automatic flow (no checklist is linked to molds; usage is never reset).
/// </summary>
public class MoldPm : AuditableEntity
{
    public int MoldPmId { get; set; }
    public string PmNo { get; set; } = string.Empty;
    public int MoldId { get; set; }
    public string Category { get; set; } = string.Empty;

    /// <summary>For the automatic PM: the IST date the PM became due (the day its threshold was reached).</summary>
    public DateOnly ScheduledDate { get; set; }

    public DateOnly? CompletedDate { get; set; }
    public int? EngineerId { get; set; }
    public int? ChecklistId { get; set; }

    /// <summary>The mold's cumulative usage when the PM was created (the trigger usage - may be past the threshold).</summary>
    public int MoldUsageAtService { get; set; }

    /// <summary>The cycle threshold that made the PM due (cycle start + interval).</summary>
    public int? ThresholdShots { get; set; }

    /// <summary>The PM interval at that moment (the master value may change later).</summary>
    public int? IntervalShots { get; set; }

    /// <summary>The mold's cumulative usage when the PM was completed ("usage at last maintenance").</summary>
    public int? UsageAtCompletion { get; set; }

    public bool UsageReset { get; set; }
    public int? UsageBeforeReset { get; set; }
    public string? MaintenanceBy { get; set; }
    public string? Remarks { get; set; }
    public string Status { get; set; } = string.Empty;

    public Mold Mold { get; set; } = null!;
}
