using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// THE one place that turns a mold's usage into automatic Mold PM work. Called by every path that changes a mold's usage
/// or PM configuration - Production Entry, the Mold master update (usage correction, interval / warning change,
/// reactivation) and the Mold PM completion (new cycle) - inside that path's transaction, with the mold row locked.
/// </summary>
public interface IMoldPmEvaluator
{
    /// <summary>
    /// Evaluates the locked mold (never modifies it): creates the cycle's ONE automatic PM when the threshold is reached and
    /// no PM is open, or raises the cycle's ONE warning notification. Runs in the caller's transaction; a notification
    /// failure never fails it.
    /// </summary>
    Task<MoldPmEvaluation> EvaluateUsageAsync(Mold lockedMold, CancellationToken cancellationToken);

    /// <summary>
    /// After the caller's transaction has COMMITTED: audits what the evaluation did as a system action ("System"),
    /// naming <paramref name="triggeredBy"/>. Never throws for an audit failure.
    /// </summary>
    Task WriteAuditAsync(MoldPmEvaluation evaluation, string triggeredBy, string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>What one evaluation found and did.</summary>
public sealed class MoldPmEvaluation
{
    public static readonly MoldPmEvaluation Nothing = new();

    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public int CurrentShots { get; init; }
    public int? IntervalShots { get; init; }
    public int? WarningShots { get; init; }
    public int CycleStartShots { get; init; }
    public long ThresholdShots { get; init; }
    public long RemainingShots { get; init; }

    /// <summary>The automatic PM created by this evaluation, if any.</summary>
    public MoldPm? CreatedPm { get; init; }

    /// <summary>The cycle's warning notification was written by this evaluation.</summary>
    public bool WarningRaised { get; init; }

    /// <summary>The PM-due notification was written by this evaluation.</summary>
    public bool DueNotified { get; init; }

    /// <summary>A notification could not be written (logged; the business work still committed).</summary>
    public bool NotificationFailed { get; init; }
}
