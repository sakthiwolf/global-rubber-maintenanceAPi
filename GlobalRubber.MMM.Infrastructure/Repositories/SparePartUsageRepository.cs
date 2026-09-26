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

public sealed class SparePartUsageRepository : ISparePartUsageRepository
{
    private const string ConcurrencyMessage = "The spare part usage was modified by another user. Refresh it and try again.";
    private const int LookupLimit = 500; // open, due maintenance is a short list; the cap only guards against surprises

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public SparePartUsageRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    private IQueryable<SparePartUsage> WithNavigations() =>
        _dbContext.SparePartUsages.AsNoTracking()
            .Include(u => u.SparePart)
            .Include(u => u.Machine)
            .Include(u => u.Mold)
            .Include(u => u.MachinePm)
            .Include(u => u.MoldPm)
            .Include(u => u.UsedByEmployee);

    public async Task<(IReadOnlyList<SparePartUsage> Items, int TotalCount)> GetAllAsync(SparePartUsageListQuery query, CancellationToken cancellationToken)
    {
        var q = _dbContext.SparePartUsages.AsNoTracking();

        if (query.DateFrom is { } from) q = q.Where(u => u.UsageDate >= from);
        if (query.DateTo is { } to) q = q.Where(u => u.UsageDate <= to);
        if (query.SparePartId is { } partId) q = q.Where(u => u.SparePartId == partId);
        if (query.MachineId is { } machineId) q = q.Where(u => u.MachineId == machineId);
        if (query.MoldId is { } moldId) q = q.Where(u => u.MoldId == moldId);
        if (!string.IsNullOrWhiteSpace(query.MaintenanceType))
        {
            var usedFor = SparePartUsageMaintenanceType.UsedForOf(query.MaintenanceType);
            q = q.Where(u => u.UsedFor == usedFor);
        }
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(u => u.Status == query.Status);

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lowered = search.ToLower();
            q = q.Where(u => u.UsageNo.ToLower().Contains(lowered) || (u.ReferenceNo != null && u.ReferenceNo.ToLower().Contains(lowered)));
        }

        var total = await q.CountAsync(cancellationToken);
        var ids = await q.OrderByDescending(u => u.UsageDate).ThenByDescending(u => u.SparePartUsageId)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(u => u.SparePartUsageId).ToListAsync(cancellationToken);

        var rows = await WithNavigations().Where(u => ids.Contains(u.SparePartUsageId)).ToListAsync(cancellationToken);
        return (ids.Select(id => rows.Single(r => r.SparePartUsageId == id)).ToList(), total);
    }

    public Task<SparePartUsage?> GetByIdAsync(int sparePartUsageId, CancellationToken cancellationToken) =>
        WithNavigations().FirstOrDefaultAsync(u => u.SparePartUsageId == sparePartUsageId, cancellationToken);

    public async Task<IReadOnlyList<SparePartStockTransaction>> GetStockMovementsAsync(int sparePartUsageId, CancellationToken cancellationToken) =>
        await _dbContext.SparePartStockTransactions.AsNoTracking()
            .Where(t => t.ReferenceType == SparePartStockReferenceType.SparePartUsage && t.ReferenceId == sparePartUsageId)
            .OrderBy(t => t.StockTransactionId)
            .ToListAsync(cancellationToken);

    public Task<SparePartUsage?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        WithNavigations().FirstOrDefaultAsync(u => u.RequestId == requestId, cancellationToken);

    public async Task<SparePartUsageLookupsDto> GetLookupsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var parts = await _dbContext.SpareParts.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SparePartCode)
            .Select(p => new UsageSparePartLookupDto
            {
                SparePartId = p.SparePartId, SparePartCode = p.SparePartCode, SparePartName = p.SparePartName, Unit = p.Unit,
                CurrentStock = p.CurrentStock, MinimumStock = p.MinimumStock, StockStatus = p.StockStatus,
            }).ToListAsync(cancellationToken);

        var machinePms = await _dbContext.MachinePms.AsNoTracking()
            .Where(p => p.Status != MachinePmStatus.Completed && p.ScheduledDate <= today)
            .OrderBy(p => p.ScheduledDate).ThenBy(p => p.MachinePmId).Take(LookupLimit)
            .Select(p => new UsageMaintenanceLookupDto
            {
                MaintenanceId = p.MachinePmId, MaintenanceNo = p.PmNo, AssetCode = p.Machine.MachineCode, AssetName = p.Machine.MachineName,
                ScheduledDate = p.ScheduledDate, Status = p.Status,
            }).ToListAsync(cancellationToken);

        var moldPms = await _dbContext.MoldPms.AsNoTracking()
            .Where(p => p.Status != MoldPmStatus.Completed)
            .OrderBy(p => p.ScheduledDate).ThenBy(p => p.MoldPmId).Take(LookupLimit)
            .Select(p => new UsageMaintenanceLookupDto
            {
                MaintenanceId = p.MoldPmId, MaintenanceNo = p.PmNo, AssetCode = p.Mold.MoldCode, AssetName = p.Mold.MoldName,
                ScheduledDate = p.ScheduledDate, Status = p.Status,
            }).ToListAsync(cancellationToken);

        var employees = await _dbContext.Employees.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.EmployeeName)
            .Select(e => new UsageEmployeeLookupDto { EmployeeId = e.EmployeeId, EmployeeCode = e.EmployeeCode, EmployeeName = e.EmployeeName })
            .ToListAsync(cancellationToken);

        return new SparePartUsageLookupsDto { SpareParts = parts, MachinePms = machinePms, MoldPms = moldPms, Employees = employees };
    }

    public async Task<SparePartUsage> AddAsync(
        SparePartUsage usage, string maintenanceType, int maintenanceId,
        Func<SparePart, MaintenanceReference?, SparePartStockTransaction> applyToLocked,
        Func<SparePart, SparePartUsage, CancellationToken, Task>? afterSave,
        CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside the
        // execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        SparePart? part = null;
        SparePartStockTransaction? ledger = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachAll(); // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

                foreach (var tracked in _dbContext.ChangeTracker.Entries<SparePart>().Where(e => e.Entity.SparePartId == usage.SparePartId).ToList())
                {
                    tracked.State = EntityState.Detached;
                }

                // 1. Lock the spare part: a concurrent issue of the SAME part waits here until this one commits, so the stock
                //    check and the deduction can never interleave (5 in stock, 4 + 3 at once -> exactly one succeeds).
                part = await _dbContext.SpareParts
                    .FromSqlInterpolated($"SELECT * FROM masters.spare_part_master WITH (UPDLOCK, ROWLOCK) WHERE spare_part_id = {usage.SparePartId}")
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(nameof(SparePart), usage.SparePartId);

                // 2. Read the maintenance record with an update lock: it cannot be completed underneath this issue.
                var pm = await LoadMaintenanceAsync(maintenanceType, maintenanceId, cancellationToken);

                // 3. Business rules (may throw: nothing has been written yet).
                ledger = applyToLocked(part, pm);

                // 4. Usage (number from the SPU sequence) + stock, then the ledger row that explains the change.
                usage.UsageNo = await _documentSequence.NextCodeAsync(DocumentTypes.SparePartUsage, cancellationToken);
                _dbContext.SparePartUsages.Add(usage);
                await _dbContext.SaveChangesAsync(cancellationToken); // INSERT usage + UPDATE stock

                ledger.ReferenceId = usage.SparePartUsageId;
                ledger.ReferenceNo = usage.ReferenceNo is null ? usage.UsageNo : $"{usage.UsageNo} ({usage.ReferenceNo})";
                _dbContext.SparePartStockTransactions.Add(ledger);
                await _dbContext.SaveChangesAsync(cancellationToken);

                // 5. Notifications (savepoint inside) - same transaction.
                if (afterSave is not null)
                {
                    await afterSave(part, usage, cancellationToken);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            DetachAll();
            if (usage.RequestId is { } requestId && sql.Message.Contains("UX_spare_part_usage_transaction_request_id", StringComparison.OrdinalIgnoreCase))
            {
                throw new DuplicateRequestException(requestId);
            }

            // usage_no is issued by the sequence: a collision means the sequence and the table disagree.
            throw new ConflictException("The spare part usage number could not be issued because it already exists. Please try again.");
        }
        catch
        {
            DetachAll();
            throw;
        }

        DetachAll();
        usage.SparePart = part!;
        return usage;

        void DetachAll()
        {
            if (_dbContext.Entry(usage).State != EntityState.Detached) _dbContext.Entry(usage).State = EntityState.Detached;
            if (part is not null) _dbContext.Entry(part).State = EntityState.Detached;
            if (ledger is not null) _dbContext.Entry(ledger).State = EntityState.Detached;
        }
    }

    public async Task<SparePartUsage> ReverseAsync(
        int sparePartUsageId, byte[] originalRowVersion,
        Func<SparePartUsage, SparePart, SparePartStockTransaction> applyToLocked, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        SparePart? part = null;
        SparePartUsage? usage = null;
        SparePartStockTransaction? ledger = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                DetachAll();

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

                var partId = await _dbContext.SparePartUsages.AsNoTracking().Where(u => u.SparePartUsageId == sparePartUsageId)
                    .Select(u => (int?)u.SparePartId).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(nameof(SparePartUsage), sparePartUsageId);

                foreach (var tracked in _dbContext.ChangeTracker.Entries<SparePart>().Where(e => e.Entity.SparePartId == partId).ToList())
                {
                    tracked.State = EntityState.Detached;
                }

                // Same lock order as an issue: spare part first, then the usage.
                part = await _dbContext.SpareParts
                    .FromSqlInterpolated($"SELECT * FROM masters.spare_part_master WITH (UPDLOCK, ROWLOCK) WHERE spare_part_id = {partId}")
                    .SingleAsync(cancellationToken);
                usage = await _dbContext.SparePartUsages
                    .FromSqlInterpolated($"SELECT * FROM transactions.spare_part_usage_transaction WITH (UPDLOCK, ROWLOCK) WHERE spare_part_usage_id = {sparePartUsageId}")
                    .SingleAsync(cancellationToken);

                // The CALLER's row version is the one the UPDATE must match (stale -> 409).
                _dbContext.Entry(usage).Property(u => u.RowVersion).OriginalValue = originalRowVersion;

                ledger = applyToLocked(usage, part);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken); // UPDATE usage (row version guarded) + UPDATE stock
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConflictException(ConcurrencyMessage);
                }

                _dbContext.SparePartStockTransactions.Add(ledger);
                await _dbContext.SaveChangesAsync(cancellationToken);

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

        usage!.SparePart = part!;
        return usage;

        void DetachAll()
        {
            if (usage is not null) _dbContext.Entry(usage).State = EntityState.Detached;
            if (part is not null) _dbContext.Entry(part).State = EntityState.Detached;
            if (ledger is not null) _dbContext.Entry(ledger).State = EntityState.Detached;
        }
    }

    private async Task<MaintenanceReference?> LoadMaintenanceAsync(string maintenanceType, int maintenanceId, CancellationToken cancellationToken)
    {
        if (maintenanceType == SparePartUsageMaintenanceType.MachinePm)
        {
            var pm = await _dbContext.MachinePms
                .FromSqlInterpolated($"SELECT * FROM transactions.machine_pm_transaction WITH (UPDLOCK, ROWLOCK) WHERE machine_pm_id = {maintenanceId}")
                .Select(p => new { p.MachinePmId, p.PmNo, p.Status, p.ScheduledDate, p.MachineId, p.Machine.MachineCode })
                .SingleOrDefaultAsync(cancellationToken);
            return pm is null ? null : new MaintenanceReference(pm.MachinePmId, pm.PmNo, pm.Status, pm.ScheduledDate, pm.MachineId, null, pm.MachineCode);
        }

        var mold = await _dbContext.MoldPms
            .FromSqlInterpolated($"SELECT * FROM transactions.mold_pm_transaction WITH (UPDLOCK, ROWLOCK) WHERE mold_pm_id = {maintenanceId}")
            .Select(p => new { p.MoldPmId, p.PmNo, p.Status, p.ScheduledDate, p.MoldId, p.Mold.MoldCode })
            .SingleOrDefaultAsync(cancellationToken);
        return mold is null ? null : new MaintenanceReference(mold.MoldPmId, mold.PmNo, mold.Status, mold.ScheduledDate, null, mold.MoldId, mold.MoldCode);
    }
}
