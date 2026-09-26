using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class MoldPmRepository : IMoldPmRepository
{
    private const string ConcurrencyMessage = "The maintenance record was modified by another user. Refresh it and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MoldPmRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<MoldPm> Items, int TotalCount)> GetAllAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var query = request.Bucket switch
        {
            MoldPmBucket.Due => Filtered(request).Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
            MoldPmBucket.Overdue => Filtered(request).Where(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
            MoldPmBucket.InProgress => Filtered(request).Where(p => p.Status == MoldPmStatus.InProgress),
            MoldPmBucket.Completed => Filtered(request).Where(p => p.Status == MoldPmStatus.Completed),
            _ => Filtered(request),
        };

        // Open work oldest-due first (the most overdue on top); completed work newest first.
        var ordered = request.Bucket == MoldPmBucket.Completed
            ? query.OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MoldPmId)
            : query.OrderBy(p => p.ScheduledDate).ThenBy(p => p.MoldPmId);

        var totalCount = await ordered.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(p => p.Mold)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken)
    {
        var counts = await Filtered(request)
            .GroupBy(_ => 1)
            .Select(g => new MoldPmCountsDto
            {
                Due = g.Count(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate >= today),
                Overdue = g.Count(p => p.Status == MoldPmStatus.Scheduled && p.ScheduledDate < today),
                InProgress = g.Count(p => p.Status == MoldPmStatus.InProgress),
                Completed = g.Count(p => p.Status == MoldPmStatus.Completed),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return counts ?? new MoldPmCountsDto();
    }

    public Task<MoldPm?> GetByIdAsync(int moldPmId, CancellationToken cancellationToken) =>
        _dbContext.MoldPms.AsNoTracking().Include(p => p.Mold).FirstOrDefaultAsync(p => p.MoldPmId == moldPmId, cancellationToken);

    public async Task<IReadOnlyList<MoldUsageSnapshot>> GetMoldUsageAsync(CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Molds.AsNoTracking()
            .OrderBy(m => m.MoldCode)
            .Select(m => new
            {
                m.MoldId, m.MoldCode, m.MoldName, m.Status, m.CurrentUsageShots, m.MaintenanceFrequencyShots, m.PmWarningShots, m.PmCycleStartShots,
                OpenPm = _dbContext.MoldPms
                    .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed)
                    .Select(p => new { p.MoldPmId, p.PmNo, p.Status, p.ScheduledDate })
                    .FirstOrDefault(),
                LastPm = _dbContext.MoldPms
                    .Where(p => p.MoldId == m.MoldId && p.Category == MoldPmCategory.ShotBased && p.Status == MoldPmStatus.Completed)
                    .OrderByDescending(p => p.CompletedDate).ThenByDescending(p => p.MoldPmId)
                    .Select(p => new { p.UsageAtCompletion, p.CompletedDate })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(m => new MoldUsageSnapshot(
            m.MoldId, m.MoldCode, m.MoldName, m.Status, m.CurrentUsageShots, m.MaintenanceFrequencyShots, m.PmWarningShots, m.PmCycleStartShots,
            m.LastPm?.UsageAtCompletion, m.LastPm?.CompletedDate,
            m.OpenPm?.MoldPmId, m.OpenPm?.PmNo, m.OpenPm?.Status, m.OpenPm?.ScheduledDate)).ToList();
    }

    public Task<bool> HasOpenAutomaticPmAsync(int moldId, CancellationToken cancellationToken) =>
        _dbContext.MoldPms.AnyAsync(
            p => p.MoldId == moldId && p.Category == MoldPmCategory.ShotBased && p.Status != MoldPmStatus.Completed, cancellationToken);

    public async Task<MoldPm> AddAutomaticPmAsync(MoldPm pm, CancellationToken cancellationToken)
    {
        // Joins the caller's transaction (the mold row is locked there): the sequence number, the insert and the caller's
        // own writes commit or roll back together.
        pm.PmNo = await _documentSequence.NextCodeAsync(DocumentTypes.MoldPm, cancellationToken);
        _dbContext.MoldPms.Add(pm);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Cannot happen while the mold row is locked; the unique indexes are the backstop - and the caller's whole
            // transaction (e.g. the production entry) rolls back rather than leave two PMs for one cycle.
            throw new ConflictException("An automatic maintenance record for this mold and cycle already exists. Please try again.");
        }
        finally
        {
            _dbContext.Entry(pm).State = EntityState.Detached;
        }

        return pm;
    }

    public async Task<MoldPm> StartAsync(MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, CancellationToken cancellationToken)
    {
        return await UpdateWithLockedMoldAsync(pm, originalRowVersion, applyToLockedMold, afterSave: null,
            new[] { nameof(MoldPm.Status), nameof(MoldPm.UpdatedAt), nameof(MoldPm.UpdatedBy) }, cancellationToken);
    }

    public async Task<MoldPm> CompleteAsync(
        MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task> afterSave,
        CancellationToken cancellationToken)
    {
        return await UpdateWithLockedMoldAsync(pm, originalRowVersion, applyToLockedMold, afterSave,
            new[]
            {
                nameof(MoldPm.Status), nameof(MoldPm.CompletedDate), nameof(MoldPm.MaintenanceBy), nameof(MoldPm.Remarks),
                nameof(MoldPm.UsageAtCompletion), nameof(MoldPm.UpdatedAt), nameof(MoldPm.UpdatedBy),
            }, cancellationToken);
    }

    // One transaction: 1. lock the mold row (UPDLOCK - the same lock Production Entry takes, one lock order: mold, then PM);
    // 2. the caller's changes to the locked mold; 3. write ONLY the given PM columns, the caller's row version being the
    // ORIGINAL value in the UPDATE's WHERE clause (stale -> 409, nothing committed); 4. afterSave (evaluation); commit.
    private async Task<MoldPm> UpdateWithLockedMoldAsync(
        MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task>? afterSave,
        IReadOnlyList<string> pmColumns, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Mold? mold = null;
        MoldPm? header = null;
        byte[] newRowVersion = Array.Empty<byte>();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachAll();

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

                foreach (var tracked in _dbContext.ChangeTracker.Entries<Mold>().Where(e => e.Entity.MoldId == pm.MoldId).ToList())
                {
                    tracked.State = EntityState.Detached;
                }

                mold = await _dbContext.Molds
                    .FromSqlInterpolated($"SELECT * FROM masters.mold_master WITH (UPDLOCK, ROWLOCK) WHERE mold_id = {pm.MoldId}")
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(nameof(Mold), pm.MoldId);

                applyToLockedMold(mold);

                header = new MoldPm
                {
                    MoldPmId = pm.MoldPmId,
                    RowVersion = originalRowVersion,
                    Status = pm.Status,
                    CompletedDate = pm.CompletedDate,
                    MaintenanceBy = pm.MaintenanceBy,
                    Remarks = pm.Remarks,
                    UsageAtCompletion = pm.UsageAtCompletion,
                    UpdatedAt = pm.UpdatedAt,
                    UpdatedBy = pm.UpdatedBy,
                };
                var entry = _dbContext.Attach(header);
                foreach (var property in pmColumns)
                {
                    entry.Property(property).IsModified = true;
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken); // UPDATE PM (row version guarded) + UPDATE mold
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConflictException(ConcurrencyMessage); // nothing committed: the transaction rolls back
                }

                newRowVersion = header.RowVersion;

                if (afterSave is not null)
                {
                    await afterSave(mold, cancellationToken);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        finally
        {
            DetachAll();
        }

        pm.RowVersion = newRowVersion;
        pm.Mold = mold!;
        return pm;

        void DetachAll()
        {
            if (header is not null) _dbContext.Entry(header).State = EntityState.Detached;
            if (mold is not null) _dbContext.Entry(mold).State = EntityState.Detached;
        }
    }

    private IQueryable<MoldPm> Filtered(MoldPmListQuery request)
    {
        var query = _dbContext.MoldPms.AsNoTracking();

        if (request.MoldId is { } moldId)
        {
            query = query.Where(p => p.MoldId == moldId);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lowered = search.ToLower();
            query = query.Where(p => p.PmNo.ToLower().Contains(lowered)
                                     || p.Mold.MoldCode.ToLower().Contains(lowered)
                                     || p.Mold.MoldName.ToLower().Contains(lowered));
        }

        return query;
    }
}
