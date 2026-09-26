using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>A clock the test can move (IST "today" = the date of UtcNow here).</summary>
internal sealed class MovableClock : IDateTimeProvider
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow);
}

/// <summary>
/// Behaves like the real MoldPmRepository where it matters: the PMs live in a list; molds are the SAME list the other
/// fakes use (so a completion's cycle start is visible to Production Entry); MPMD-NNNN numbers; the open-PM and
/// per-threshold uniqueness the database enforces; start / complete run the callbacks on a COPY of the mold and commit
/// only if everything (including afterSave) succeeds; a stale row version -> 409.
/// </summary>
internal sealed class InMemoryMoldPmRepository : IMoldPmRepository
{
    private readonly List<Mold> _molds;
    private int _lastNumber;
    private int _nextRowVersion = 1;

    public InMemoryMoldPmRepository(List<Mold> molds)
    {
        _molds = molds;
    }

    public List<MoldPm> Pms { get; } = new();

    private MoldPm Copy(MoldPm p) => new()
    {
        MoldPmId = p.MoldPmId, PmNo = p.PmNo, MoldId = p.MoldId, Category = p.Category, ScheduledDate = p.ScheduledDate,
        CompletedDate = p.CompletedDate, MoldUsageAtService = p.MoldUsageAtService, ThresholdShots = p.ThresholdShots,
        IntervalShots = p.IntervalShots, UsageAtCompletion = p.UsageAtCompletion, MaintenanceBy = p.MaintenanceBy, Remarks = p.Remarks,
        Status = p.Status, CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy, UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy,
        RowVersion = p.RowVersion.ToArray(), Mold = _molds.Single(m => m.MoldId == p.MoldId),
    };

    private IEnumerable<MoldPm> Filtered(MoldPmListQuery q) =>
        Pms.Where(p => q.MoldId is null || p.MoldId == q.MoldId)
           .Where(p => string.IsNullOrWhiteSpace(q.Search) || p.PmNo.Contains(q.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                       || _molds.Single(m => m.MoldId == p.MoldId).MoldCode.Contains(q.Search.Trim(), StringComparison.OrdinalIgnoreCase));

    public Task<(IReadOnlyList<MoldPm> Items, int TotalCount)> GetAllAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = request.Bucket switch
        {
            MoldPmBucket.Due => Filtered(request).Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
            MoldPmBucket.Overdue => Filtered(request).Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            MoldPmBucket.InProgress => Filtered(request).Where(p => p.Status == MoldPmStatus.InProgress),
            MoldPmBucket.Completed => Filtered(request).Where(p => p.Status == MoldPmStatus.Completed),
            _ => Filtered(request),
        };
        var all = rows.OrderBy(p => p.ScheduledDate).ThenBy(p => p.MoldPmId).ToList();
        IReadOnlyList<MoldPm> page = all.Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).Select(Copy).ToList();
        return Task.FromResult((page, all.Count));
    }

    public Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = Filtered(request).ToList();
        return Task.FromResult(new MoldPmCountsDto
        {
            Due = rows.Count(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
            Overdue = rows.Count(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            InProgress = rows.Count(p => p.Status == MoldPmStatus.InProgress),
            Completed = rows.Count(p => p.Status == MoldPmStatus.Completed),
        });
    }

    public Task<MoldPm?> GetByIdAsync(int moldPmId, CancellationToken cancellationToken) =>
        Task.FromResult(Pms.Where(p => p.MoldPmId == moldPmId).Select(Copy).FirstOrDefault());

    public Task<IReadOnlyList<MoldUsageSnapshot>> GetMoldUsageAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MoldUsageSnapshot> rows = _molds.OrderBy(m => m.MoldCode).Select(m =>
        {
            var open = Pms.FirstOrDefault(p => p.MoldId == m.MoldId && p.Status != MoldPmStatus.Completed);
            var last = Pms.Where(p => p.MoldId == m.MoldId && p.Status == MoldPmStatus.Completed).OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MoldPmId).FirstOrDefault();
            return new MoldUsageSnapshot(m.MoldId, m.MoldCode, m.MoldName, m.Status, m.CurrentUsageShots, m.MaintenanceFrequencyShots, m.PmWarningShots,
                m.PmCycleStartShots, last?.UsageAtCompletion, last?.CompletedDate, open?.MoldPmId, open?.PmNo, open?.Status, open?.ScheduledDate);
        }).ToList();
        return Task.FromResult(rows);
    }

    public Task<bool> HasOpenAutomaticPmAsync(int moldId, CancellationToken cancellationToken) =>
        Task.FromResult(Pms.Any(p => p.MoldId == moldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed));

    public Task<MoldPm> AddAutomaticPmAsync(MoldPm pm, CancellationToken cancellationToken)
    {
        // The two filtered unique indexes of migration 014.
        if (Pms.Any(p => p.MoldId == pm.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed)
            || Pms.Any(p => p.MoldId == pm.MoldId && p.Category == MoldPmCategory.ShotBased && p.ThresholdShots == pm.ThresholdShots))
        {
            throw new ConflictException("An automatic maintenance record for this mold and cycle already exists. Please try again.");
        }

        _lastNumber++;
        pm.MoldPmId = Pms.Count + 1;
        pm.PmNo = $"MPMD-{_lastNumber:0000}";
        pm.RowVersion = new[] { (byte)_nextRowVersion++ };
        Pms.Add(Copy(pm));
        return Task.FromResult(pm);
    }

    public Task<MoldPm> StartAsync(MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, CancellationToken cancellationToken) =>
        UpdateAsync(pm, originalRowVersion, applyToLockedMold, null, cancellationToken);

    public Task<MoldPm> CompleteAsync(
        MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task> afterSave, CancellationToken cancellationToken) =>
        UpdateAsync(pm, originalRowVersion, applyToLockedMold, afterSave, cancellationToken);

    private async Task<MoldPm> UpdateAsync(
        MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        var storedMold = _molds.Single(m => m.MoldId == pm.MoldId);
        var locked = MoldCopy(storedMold);
        applyToLockedMold(locked);

        var stored = Pms.Single(p => p.MoldPmId == pm.MoldPmId);
        if (!stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The maintenance record was modified by another user. Refresh it and try again.");
        }

        // "Save" the PM + mold, then the in-transaction evaluation; roll back both if it throws.
        var pmBefore = Copy(stored);
        var moldBefore = MoldCopy(storedMold);
        stored.Status = pm.Status;
        stored.CompletedDate = pm.CompletedDate;
        stored.MaintenanceBy = pm.MaintenanceBy;
        stored.Remarks = pm.Remarks;
        stored.UsageAtCompletion = pm.UsageAtCompletion;
        stored.UpdatedAt = pm.UpdatedAt;
        stored.UpdatedBy = pm.UpdatedBy;
        stored.RowVersion = new[] { (byte)_nextRowVersion++ };
        Apply(locked, storedMold);

        try
        {
            if (afterSave is not null) await afterSave(locked, cancellationToken);
        }
        catch
        {
            Pms[Pms.IndexOf(stored)] = pmBefore;
            Apply(moldBefore, storedMold);
            throw;
        }

        pm.RowVersion = stored.RowVersion;
        pm.Mold = storedMold;
        return pm;
    }

    internal static Mold MoldCopy(Mold m) => new()
    {
        MoldId = m.MoldId, MoldCode = m.MoldCode, MoldName = m.MoldName, ProductId = m.ProductId, MoldType = m.MoldType,
        MaximumShots = m.MaximumShots, WarningShots = m.WarningShots, ReplacementShots = m.ReplacementShots,
        MaintenanceFrequencyShots = m.MaintenanceFrequencyShots, PmWarningShots = m.PmWarningShots, PmCycleStartShots = m.PmCycleStartShots,
        CurrentUsageShots = m.CurrentUsageShots, Status = m.Status, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy, RowVersion = m.RowVersion,
    };

    internal static void Apply(Mold from, Mold to)
    {
        to.CurrentUsageShots = from.CurrentUsageShots;
        to.PmCycleStartShots = from.PmCycleStartShots;
        to.Status = from.Status;
        to.UpdatedAt = from.UpdatedAt;
        to.UpdatedBy = from.UpdatedBy;
    }
}

/// <summary>Notifications in a list; the unique event key as in the database; FailNextAdd simulates a failed insert.</summary>
internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    public List<Notification> Items { get; } = new();
    public bool FailNextAdd { get; set; }

    public Task<bool> ExistsAsync(string eventKey, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(n => n.EventKey == eventKey));

    public Task<NotificationWriteResult> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (FailNextAdd)
        {
            FailNextAdd = false;
            return Task.FromResult(NotificationWriteResult.Failed);
        }

        if (Items.Any(n => n.EventKey == notification.EventKey))
        {
            return Task.FromResult(NotificationWriteResult.AlreadyExists);
        }

        notification.NotificationId = Items.Count + 1;
        notification.CreatedAt = new DateTime(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc).AddMinutes(Items.Count);
        Items.Add(notification);
        return Task.FromResult(NotificationWriteResult.Added);
    }

    public Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetForModulesAsync(
        IReadOnlyCollection<string> moduleCodes, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var all = Items.Where(n => moduleCodes.Contains(n.ModuleCode)).OrderByDescending(n => n.CreatedAt).ToList();
        IReadOnlyList<Notification> page = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult((page, all.Count));
    }
}

internal static class MoldPmTestFactory
{
    public static MoldPmEvaluator Evaluator(IMoldPmRepository pms, INotificationRepository notifications, IDateTimeProvider clock, IAuditLogService audit) =>
        new(pms, notifications, clock, audit, NullLogger<MoldPmEvaluator>.Instance);
}
