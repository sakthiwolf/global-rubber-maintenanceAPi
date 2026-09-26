using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Usage-based Mold PM evaluation (approved 2026-09-25) - the rules are <see cref="MoldPmRules"/>; this class applies them
/// to a LOCKED mold inside the caller's transaction:
/// - threshold reached, no open PM -&gt; ONE automatic 'Shot-based' PM (Scheduled, due today IST, created by the system) and
///   ONE "PM due" notification (event key per mold + threshold);
/// - otherwise inside the warning margin -&gt; ONE warning notification per cycle (event key per mold + cycle start).
/// The mold row lock serializes evaluations of the same mold; UX_mold_pm_transaction_open_shot_based,
/// UX_mold_pm_transaction_shot_based_threshold and UQ_notification_transaction_event_key are the database backstops.
/// A notification failure is logged and never fails the transaction (the repository uses a savepoint).
/// </summary>
public sealed class MoldPmEvaluator : IMoldPmEvaluator
{
    public const string SystemUserName = "System";
    public const string MoldPmLinkPath = "/transactions/mold-maintenance";

    private readonly IMoldPmRepository _moldPmRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MoldPmEvaluator> _logger;

    public MoldPmEvaluator(
        IMoldPmRepository moldPmRepository,
        INotificationRepository notificationRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MoldPmEvaluator> logger)
    {
        _moldPmRepository = moldPmRepository;
        _notificationRepository = notificationRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public static string WarningEventKey(int moldId, int cycleStartShots) =>
        string.Create(CultureInfo.InvariantCulture, $"MOLD_PM_WARNING:{moldId}:{cycleStartShots}");

    public static string DueEventKey(int moldId, long thresholdShots) =>
        string.Create(CultureInfo.InvariantCulture, $"MOLD_PM_DUE:{moldId}:{thresholdShots}");

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public async Task<MoldPmEvaluation> EvaluateUsageAsync(Mold lockedMold, CancellationToken cancellationToken)
    {
        // Cheap exits first: nothing to look up for a disabled or Retired mold.
        if (!MoldPmRules.IsEnabled(lockedMold.MaintenanceFrequencyShots) || lockedMold.Status == MoldStatus.Retired)
        {
            return MoldPmEvaluation.Nothing;
        }

        var hasOpenPm = await _moldPmRepository.HasOpenAutomaticPmAsync(lockedMold.MoldId, cancellationToken);
        var decision = MoldPmRules.Decide(
            lockedMold.MaintenanceFrequencyShots, lockedMold.PmCycleStartShots, lockedMold.PmWarningShots,
            lockedMold.CurrentUsageShots, lockedMold.Status, hasOpenPm);

        if (!decision.CreatePm && !decision.RaiseWarning)
        {
            return MoldPmEvaluation.Nothing;
        }

        var interval = lockedMold.MaintenanceFrequencyShots!.Value;
        MoldPm? created = null;
        var warningRaised = false;
        var dueNotified = false;
        var notificationFailed = false;

        if (decision.CreatePm)
        {
            created = await _moldPmRepository.AddAutomaticPmAsync(new MoldPm
            {
                MoldId = lockedMold.MoldId,
                Category = MoldPmCategory.ShotBased,
                ScheduledDate = _dateTimeProvider.Today, // the IST date it became due - not a future calendar date
                MoldUsageAtService = lockedMold.CurrentUsageShots,
                ThresholdShots = checked((int)decision.ThresholdShots),
                IntervalShots = interval,
                Status = MoldPmStatus.Scheduled,
                CreatedAt = _dateTimeProvider.UtcNow,
                CreatedBy = null, // created by the system
            }, cancellationToken);
            created.Mold = lockedMold;

            var result = await _notificationRepository.TryAddAsync(new Notification
            {
                NotificationType = NotificationTypes.MoldPmDue,
                ModuleCode = ModuleCodes.TrnMoldPm,
                Severity = NotificationSeverity.Critical,
                Title = $"Mold PM due: {lockedMold.MoldCode}",
                Message = Truncate(
                    $"Mold {lockedMold.MoldCode} - {lockedMold.MoldName} reached its preventive maintenance threshold. " +
                    $"Current shots: {N(lockedMold.CurrentUsageShots)}; threshold: {N(decision.ThresholdShots)} (interval {N(interval)}). " +
                    $"Mold PM {created.PmNo} was created automatically.", 500),
                EntityName = MoldPmAuditNames.EntityName,
                EntityId = created.MoldPmId,
                RecordRef = lockedMold.MoldCode,
                LinkPath = MoldPmLinkPath,
                EventKey = DueEventKey(lockedMold.MoldId, decision.ThresholdShots),
                CreatedBy = null,
            }, cancellationToken);
            dueNotified = result == NotificationWriteResult.Added;
            notificationFailed = result == NotificationWriteResult.Failed;
        }
        else
        {
            var eventKey = WarningEventKey(lockedMold.MoldId, lockedMold.PmCycleStartShots);
            if (!await _notificationRepository.ExistsAsync(eventKey, cancellationToken))
            {
                var result = await _notificationRepository.TryAddAsync(new Notification
                {
                    NotificationType = NotificationTypes.MoldPmWarning,
                    ModuleCode = ModuleCodes.TrnMoldPm,
                    Severity = NotificationSeverity.Warning,
                    Title = $"Mold {lockedMold.MoldCode} approaching preventive maintenance",
                    Message = Truncate(
                        $"Mold {lockedMold.MoldCode} - {lockedMold.MoldName} is approaching preventive maintenance. " +
                        $"Remaining shots: {N(decision.RemainingShots)}. Current shots: {N(lockedMold.CurrentUsageShots)}; " +
                        $"threshold: {N(decision.ThresholdShots)} (interval {N(interval)}); warning at {N(lockedMold.PmWarningShots ?? 0)} remaining.", 500),
                    EntityName = MoldPmAuditNames.MoldEntityName,
                    EntityId = lockedMold.MoldId,
                    RecordRef = lockedMold.MoldCode,
                    LinkPath = MoldPmLinkPath,
                    EventKey = eventKey,
                    CreatedBy = null,
                }, cancellationToken);
                warningRaised = result == NotificationWriteResult.Added;
                notificationFailed = result == NotificationWriteResult.Failed;
            }
        }

        if (notificationFailed)
        {
            _logger.LogWarning("A Mold PM notification for mold {MoldCode} could not be written; the maintenance work was saved.", lockedMold.MoldCode);
        }

        return new MoldPmEvaluation
        {
            MoldId = lockedMold.MoldId,
            MoldCode = lockedMold.MoldCode,
            MoldName = lockedMold.MoldName,
            CurrentShots = lockedMold.CurrentUsageShots,
            IntervalShots = interval,
            WarningShots = lockedMold.PmWarningShots,
            CycleStartShots = lockedMold.PmCycleStartShots,
            ThresholdShots = decision.ThresholdShots,
            RemainingShots = decision.RemainingShots,
            CreatedPm = created,
            WarningRaised = warningRaised,
            DueNotified = dueNotified,
            NotificationFailed = notificationFailed,
        };
    }

    public async Task WriteAuditAsync(MoldPmEvaluation evaluation, string triggeredBy, string? ipAddress, CancellationToken cancellationToken)
    {
        if (evaluation.CreatedPm is { } pm)
        {
            await LogAsync(MoldPmAuditNames.AutoCreated, MoldPmAuditNames.EntityName, pm.MoldPmId, pm.PmNo,
                $"System automatically created mold PM {pm.PmNo} for mold {evaluation.MoldCode}: usage {N(evaluation.CurrentShots)} reached the " +
                $"threshold {N(evaluation.ThresholdShots)} (interval {N(evaluation.IntervalShots ?? 0)}). Triggered by {triggeredBy}.",
                new List<AuditLogDetailEntry>
                {
                    new("status", null, MoldPmStatus.Scheduled),
                    new("usage_at_trigger", null, Raw(evaluation.CurrentShots)),
                    new("threshold_shots", null, Raw(evaluation.ThresholdShots)),
                    new("interval_shots", null, Raw(evaluation.IntervalShots ?? 0)),
                    new("scheduled_date", null, pm.ScheduledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                }, ipAddress, cancellationToken);
        }

        if (evaluation.WarningRaised)
        {
            await LogAsync(MoldPmAuditNames.WarningRaised, MoldPmAuditNames.MoldEntityName, evaluation.MoldId, evaluation.MoldCode,
                $"System raised the preventive maintenance warning for mold {evaluation.MoldCode}: {N(evaluation.RemainingShots)} shots remaining " +
                $"(usage {N(evaluation.CurrentShots)}, threshold {N(evaluation.ThresholdShots)}, warning at {N(evaluation.WarningShots ?? 0)}). Triggered by {triggeredBy}.",
                new List<AuditLogDetailEntry>
                {
                    new("current_usage_shots", null, Raw(evaluation.CurrentShots)),
                    new("threshold_shots", null, Raw(evaluation.ThresholdShots)),
                    new("remaining_shots", null, Raw(evaluation.RemainingShots)),
                    new("pm_warning_shots", null, Raw(evaluation.WarningShots ?? 0)),
                }, ipAddress, cancellationToken);
        }
    }

    private static string Raw(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // A system action: no user id, user name "System" - distinguishable from user actions. AuditLogService swallows its
    // own persistence errors, so an audit failure never turns the committed work into a failure.
    private Task LogAsync(
        string action, string entityName, int entityId, string recordRef, string description,
        IReadOnlyList<AuditLogDetailEntry> details, string? ipAddress, CancellationToken cancellationToken) =>
        _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = null,
            UserName = SystemUserName,
            Module = MoldPmAuditNames.Module,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            RecordRef = recordRef,
            Description = description.Length <= 1000 ? description : description[..1000],
            IpAddress = ipAddress,
            Details = details,
        }, cancellationToken);
}
