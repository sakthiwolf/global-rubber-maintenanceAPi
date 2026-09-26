using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Mold Preventive Maintenance - automatic and usage-based (approved 2026-09-25). A Mold PM is never scheduled by hand:
/// <see cref="IMoldPmEvaluator"/> creates it when the mold's cumulative usage reaches its cycle threshold. This service
/// lists them (tabs due / overdue / in-progress / completed), shows every mold's PM position, and runs the workflow:
///
/// Start (template 6.4 "Start Maintenance"): Scheduled -&gt; In Progress; the mold's status becomes Maintenance (a Retired
/// mold keeps Retired). Complete: Maintenance By required (free text, at most 100, as Machine PM), remarks optional (at
/// most 1000), completed on today's plant (IST) date; in the SAME transaction, with the mold row locked, the usage at
/// completion is recorded, the mold's cycle is re-anchored on the PM's own threshold (MoldPmRules.CycleStartAfterCompletion
/// - late maintenance never shifts the cycle, missed cycles create no backlog), a mold in Maintenance returns to In
/// Production (BR-25; other statuses are kept), and the new cycle is evaluated (its warning may already apply). The usage
/// counter itself is never reset. Both actions need the caller's row version (stale -&gt; 409).
/// </summary>
public sealed class MoldPmService : IMoldPmService
{
    private const int MaintenanceByMaxLength = 100; // maintenance_by NVARCHAR(100)
    private const int RemarksMaxLength = 1000;      // remarks NVARCHAR(1000)

    private readonly IMoldPmRepository _repository;
    private readonly IMoldPmEvaluator _evaluator;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MoldPmService> _logger;

    public MoldPmService(
        IMoldPmRepository repository,
        IMoldPmEvaluator evaluator,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MoldPmService> logger)
    {
        _repository = repository;
        _evaluator = evaluator;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MoldPmDto>> GetAllAsync(MoldPmListQuery query, CancellationToken cancellationToken)
    {
        ValidateBucket(query);
        var today = _dateTimeProvider.Today;
        var (items, totalCount) = await _repository.GetAllAsync(query, today, cancellationToken);

        return PagedResult<MoldPmDto>.Create(items.Select(pm => MapToDto(pm, today)).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery query, CancellationToken cancellationToken)
    {
        ValidateBucket(query);
        return _repository.GetCountsAsync(query, _dateTimeProvider.Today, cancellationToken);
    }

    public async Task<MoldPmDto> GetByIdAsync(int moldPmId, CancellationToken cancellationToken)
    {
        var pm = await _repository.GetByIdAsync(moldPmId, cancellationToken)
            ?? throw new NotFoundException(nameof(MoldPm), moldPmId);

        return MapToDto(pm, _dateTimeProvider.Today);
    }

    public async Task<IReadOnlyList<MoldUsageDto>> GetMoldUsageAsync(CancellationToken cancellationToken)
    {
        var today = _dateTimeProvider.Today;
        var rows = await _repository.GetMoldUsageAsync(cancellationToken);

        return rows.Select(m =>
        {
            var enabled = MoldPmRules.IsEnabled(m.IntervalShots);
            long? threshold = enabled ? MoldPmRules.NextThreshold(m.CycleStartShots, m.IntervalShots!.Value) : null;
            return new MoldUsageDto
            {
                MoldId = m.MoldId,
                MoldCode = m.MoldCode,
                MoldName = m.MoldName,
                MoldStatus = m.MoldStatus,
                CurrentShots = m.CurrentShots,
                IntervalShots = m.IntervalShots,
                WarningShots = m.WarningShots,
                CycleStartShots = m.CycleStartShots,
                NextThresholdShots = threshold,
                RemainingShots = threshold - m.CurrentShots,
                UsageSinceCycleStart = enabled ? (long)m.CurrentShots - m.CycleStartShots : null,
                LastMaintenanceShots = m.LastMaintenanceShots,
                LastMaintenanceDate = m.LastMaintenanceDate,
                State = MoldPmRules.StateOf(m.IntervalShots, m.CycleStartShots, m.WarningShots, m.CurrentShots, m.OpenPmStatus, m.OpenPmScheduledDate, today),
                OpenPmId = m.OpenPmId,
                OpenPmNo = m.OpenPmNo,
                OpenPmStatus = m.OpenPmStatus,
                OpenPmScheduledDate = m.OpenPmScheduledDate,
            };
        }).ToList();
    }

    public async Task<MoldPmDto> StartAsync(
        int moldPmId, StartMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var pm = await _repository.GetByIdAsync(moldPmId, cancellationToken)
            ?? throw new NotFoundException(nameof(MoldPm), moldPmId);

        if (pm.Status != MoldPmStatus.Scheduled)
        {
            throw new ConflictException(pm.Status == MoldPmStatus.Completed
                ? "The maintenance record is already completed."
                : "The maintenance has already been started.");
        }

        var originalRowVersion = DecodeRequiredRowVersion(request.RowVersion, new List<string>());

        var now = _dateTimeProvider.UtcNow;
        pm.Status = MoldPmStatus.InProgress;
        pm.UpdatedAt = now;
        pm.UpdatedBy = actingUserId;

        var moldStatusBefore = string.Empty;
        var started = await _repository.StartAsync(pm, originalRowVersion, mold =>
        {
            moldStatusBefore = mold.Status;
            if (mold.Status != MoldStatus.Retired)
            {
                mold.Status = MoldStatus.Maintenance; // template 6.4 / BR-24: the mold is under maintenance
                mold.UpdatedAt = now;
                mold.UpdatedBy = actingUserId;
            }
        }, cancellationToken);

        var details = new List<AuditLogDetailEntry> { new("status", MoldPmStatus.Scheduled, MoldPmStatus.InProgress) };
        if (moldStatusBefore != started.Mold.Status)
        {
            details.Add(new AuditLogDetailEntry($"mold_status ({started.Mold.MoldCode})", moldStatusBefore, started.Mold.Status));
        }

        await WriteAuditAsync(MoldPmAuditNames.Started, started, actingUserId, ipAddress,
            actor => $"{actor} started mold PM {started.PmNo} for mold {started.Mold.MoldCode}.", details, cancellationToken);

        return MapToDto(started, _dateTimeProvider.Today);
    }

    public async Task<MoldPmDto> CompleteAsync(
        int moldPmId, CompleteMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var pm = await _repository.GetByIdAsync(moldPmId, cancellationToken)
            ?? throw new NotFoundException(nameof(MoldPm), moldPmId);

        // Explicit rather than a silent no-op: nothing is written or audited.
        if (pm.Status == MoldPmStatus.Completed)
        {
            throw new ConflictException("The maintenance record is already completed.");
        }

        var maintenanceBy = string.IsNullOrWhiteSpace(request.MaintenanceBy) ? null : request.MaintenanceBy.Trim();
        var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

        var errors = new List<string>();
        if (maintenanceBy is null) errors.Add("MaintenanceBy is required.");
        else if (maintenanceBy.Length > MaintenanceByMaxLength) errors.Add($"MaintenanceBy must be at most {MaintenanceByMaxLength} characters.");
        if (remarks is { Length: > RemarksMaxLength }) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");
        var originalRowVersion = DecodeRequiredRowVersion(request.RowVersion, errors);

        var statusBefore = pm.Status;
        var today = _dateTimeProvider.Today; // plant (IST) date - never client-controlled
        var now = _dateTimeProvider.UtcNow;
        pm.Status = MoldPmStatus.Completed;
        pm.CompletedDate = today;
        pm.MaintenanceBy = maintenanceBy;
        pm.Remarks = remarks;
        pm.UpdatedAt = now;
        pm.UpdatedBy = actingUserId;

        // Captured on the locked mold inside the transaction; used only after it has committed.
        var before = (CycleStart: 0, Status: string.Empty);
        var nextThreshold = 0L;
        var evaluation = MoldPmEvaluation.Nothing;

        var completed = await _repository.CompleteAsync(pm, originalRowVersion, mold =>
        {
            before = (mold.PmCycleStartShots, mold.Status);

            // The interval of the cycle being closed: the master's current one, or the PM's own if PM was since disabled.
            var interval = mold.MaintenanceFrequencyShots is > 0 ? mold.MaintenanceFrequencyShots.Value : pm.IntervalShots ?? 0;
            var threshold = pm.ThresholdShots ?? (mold.PmCycleStartShots + interval);

            pm.UsageAtCompletion = mold.CurrentUsageShots;
            if (interval > 0)
            {
                mold.PmCycleStartShots = MoldPmRules.CycleStartAfterCompletion(threshold, interval, mold.CurrentUsageShots);
                nextThreshold = MoldPmRules.NextThreshold(mold.PmCycleStartShots, interval);
            }

            if (mold.Status == MoldStatus.Maintenance)
            {
                mold.Status = MoldStatus.InProduction; // BR-25 - only a mold that was under maintenance is released
            }

            mold.UpdatedAt = now;
            mold.UpdatedBy = actingUserId;
        }, async (mold, ct) => evaluation = await _evaluator.EvaluateUsageAsync(mold, ct), cancellationToken);

        static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        static string Raw(long v) => v.ToString(CultureInfo.InvariantCulture);
        var code = completed.Mold.MoldCode;
        var details = new List<AuditLogDetailEntry>
        {
            new("status", statusBefore, MoldPmStatus.Completed),
            new("completed_date", null, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("maintenance_by", null, completed.MaintenanceBy),
            new("usage_at_trigger", null, Raw(completed.MoldUsageAtService)),
            new("usage_at_completion", null, Raw(completed.UsageAtCompletion ?? 0)),
            new("threshold_shots", null, completed.ThresholdShots?.ToString(CultureInfo.InvariantCulture)),
            new($"pm_cycle_start_shots ({code})", Raw(before.CycleStart), Raw(completed.Mold.PmCycleStartShots)),
            new("next_threshold_shots", null, Raw(nextThreshold)),
        };
        if (completed.Remarks is not null) details.Add(new AuditLogDetailEntry("remarks", null, completed.Remarks));
        if (before.Status != completed.Mold.Status) details.Add(new AuditLogDetailEntry($"mold_status ({code})", before.Status, completed.Mold.Status));

        await WriteAuditAsync(MoldPmAuditNames.Completed, completed, actingUserId, ipAddress,
            actor => $"{actor} completed mold PM {completed.PmNo} for mold {code} on {today:yyyy-MM-dd}, performed by {completed.MaintenanceBy}, " +
                     $"at {N(completed.UsageAtCompletion ?? 0)} shots (due at {N(completed.ThresholdShots ?? 0)}). Next maintenance threshold: {N(nextThreshold)} shots.",
            details, cancellationToken);

        await _evaluator.WriteAuditAsync(evaluation, $"completion of mold PM {completed.PmNo}", ipAddress, cancellationToken);

        return MapToDto(completed, today);
    }

    private static void ValidateBucket(MoldPmListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Bucket) && !MoldPmBucket.All.Contains(query.Bucket))
        {
            throw new ValidationException($"Bucket must be one of: {string.Join(", ", MoldPmBucket.All)}.");
        }
    }

    // Adds the row-version problems to the other field errors and throws them all together.
    private static byte[] DecodeRequiredRowVersion(string? rowVersion, List<string> errors)
    {
        byte[]? bytes = null;
        if (string.IsNullOrWhiteSpace(rowVersion)) errors.Add("RowVersion is required.");
        else if (!TryDecodeRowVersion(rowVersion, out bytes)) errors.Add("RowVersion is not valid.");

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return bytes!;
    }

    private static bool TryDecodeRowVersion(string value, out byte[]? bytes)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, MoldPm pm, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry> details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MoldPmAuditNames.Module,
            Action = action,
            EntityName = MoldPmAuditNames.EntityName,
            EntityId = pm.MoldPmId,
            RecordRef = pm.PmNo,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details,
        }, cancellationToken);
    }

    internal static MoldPmDto MapToDto(MoldPm pm, DateOnly today)
    {
        var open = pm.Status != MoldPmStatus.Completed;
        var currentShots = pm.Mold?.CurrentUsageShots ?? 0;
        return new MoldPmDto
        {
            MoldPmId = pm.MoldPmId,
            PmNo = pm.PmNo,
            MoldId = pm.MoldId,
            MoldCode = pm.Mold?.MoldCode ?? string.Empty,
            MoldName = pm.Mold?.MoldName ?? string.Empty,
            MoldStatus = pm.Mold?.Status ?? string.Empty,
            Category = pm.Category,
            Trigger = pm.Category == MoldPmCategory.ShotBased ? MoldPmAuditNames.UsageThresholdTrigger : pm.Category,
            ScheduledDate = pm.ScheduledDate,
            CompletedDate = pm.CompletedDate,
            UsageAtTrigger = pm.MoldUsageAtService,
            ThresholdShots = pm.ThresholdShots,
            IntervalShots = pm.IntervalShots,
            CurrentShots = currentShots,
            RemainingShots = open && pm.ThresholdShots is { } t ? (long)t - currentShots : null,
            UsageAtCompletion = pm.UsageAtCompletion,
            IsOverdue = pm.Status == MoldPmStatus.Scheduled && MoldPmRules.IsOverdue(pm.ScheduledDate, today),
            MaintenanceBy = pm.MaintenanceBy,
            Remarks = pm.Remarks,
            Status = pm.Status,
            CreatedBySystem = pm.CreatedBy is null,
            CreatedAt = pm.CreatedAt,
            CreatedBy = pm.CreatedBy,
            UpdatedAt = pm.UpdatedAt,
            UpdatedBy = pm.UpdatedBy,
            RowVersion = Convert.ToBase64String(pm.RowVersion),
        };
    }
}
