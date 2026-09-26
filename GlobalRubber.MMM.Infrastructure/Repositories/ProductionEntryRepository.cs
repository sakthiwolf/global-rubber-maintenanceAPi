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

public sealed class ProductionEntryRepository : IProductionEntryRepository
{
    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public ProductionEntryRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<ProductionEntry> Items, int TotalCount)> GetAllAsync(
        ProductionEntryListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.ProductionEntries.AsNoTracking();

        if (request.FromDate is { } from)
        {
            query = query.Where(e => e.EntryDate >= from);
        }

        if (request.ToDate is { } to)
        {
            query = query.Where(e => e.EntryDate <= to);
        }

        if (request.MachineId is { } machineId)
        {
            query = query.Where(e => e.MachineId == machineId);
        }

        if (request.MoldId is { } moldId)
        {
            query = query.Where(e => e.MoldId == moldId);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(e => e.EntryNo.ToLower().Contains(lowered));
        }

        // Newest first (analysis 6.1) - in the order the entries were saved, as the template lists them.
        var ordered = query.OrderByDescending(e => e.ProductionEntryId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Include(e => e.Machine)
            .Include(e => e.Product)
            .Include(e => e.Mold)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<ProductionEntry?> GetByIdAsync(int productionEntryId, CancellationToken cancellationToken) =>
        _dbContext.ProductionEntries
            .AsNoTracking()
            .Include(e => e.Machine)
            .Include(e => e.Product)
            .Include(e => e.Mold)
            .FirstOrDefaultAsync(e => e.ProductionEntryId == productionEntryId, cancellationToken);

    public async Task<ProductionLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var machines = await _dbContext.Machines.AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.MachineCode)
            .Select(m => new ProductionMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName, Location = m.Location })
            .ToListAsync(cancellationToken);

        var products = await _dbContext.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProductCode)
            .Select(p => new ProductionProductLookupDto { ProductId = p.ProductId, ProductCode = p.ProductCode, ProductName = p.ProductName })
            .ToListAsync(cancellationToken);

        // Molds of active products only - a mold of an inactive product could never be saved (the product must be
        // active). Any mold status is listed, as in the template (Q-09).
        var molds = await _dbContext.Molds.AsNoTracking()
            .Where(m => m.Product.IsActive)
            .OrderBy(m => m.MoldCode)
            .Select(m => new ProductionMoldLookupDto
            {
                MoldId = m.MoldId, MoldCode = m.MoldCode, MoldName = m.MoldName, ProductId = m.ProductId,
                CurrentUsageShots = m.CurrentUsageShots, MaximumShots = m.MaximumShots, WarningShots = m.WarningShots,
                ReplacementShots = m.ReplacementShots, LifeState = m.LifeState, Status = m.Status,
            })
            .ToListAsync(cancellationToken);

        return new ProductionLookupsDto { Machines = machines, Products = products, Molds = molds };
    }

    public async Task<ProductionEntry> AddAsync(
        ProductionEntry entry, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Mold? mold = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                Detach(entry, mold); // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                // A tracked copy would be returned as-is (stale) instead of the locked row - make sure there is none.
                foreach (var tracked in _dbContext.ChangeTracker.Entries<Mold>().Where(e => e.Entity.MoldId == entry.MoldId).ToList())
                {
                    tracked.State = EntityState.Detached;
                }

                // UPDLOCK: a second production save for the same mold waits here until this one commits, so the limit
                // check (BR-10) and the usage increment (BR-11) can never interleave.
                mold = await _dbContext.Molds
                    .FromSqlInterpolated($"SELECT * FROM masters.mold_master WITH (UPDLOCK, ROWLOCK) WHERE mold_id = {entry.MoldId}")
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException(nameof(Mold), entry.MoldId);

                applyToLockedMold(mold); // business rules - may throw, which rolls everything back

                entry.EntryNo = await _documentSequence.NextCodeAsync(DocumentTypes.ProductionEntry, cancellationToken);
                _dbContext.ProductionEntries.Add(entry);
                await _dbContext.SaveChangesAsync(cancellationToken); // INSERT entry + UPDATE mold, same transaction

                // The automatic Mold PM evaluation on the saved mold - still locked, same transaction.
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
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            Detach(entry, mold);

            // entry_no is the only unique constraint and it is issued by the sequence, not the client, so a collision
            // means the sequence and the table disagree.
            throw new ConflictException("The production entry number could not be issued because it already exists. Please try again.");
        }
        catch
        {
            Detach(entry, mold);
            throw;
        }

        // Keep the generated id/number/good qty/row_version but stop tracking.
        Detach(entry, mold);
        entry.Mold = mold!;
        return entry;
    }

    private void Detach(ProductionEntry entry, Mold? mold)
    {
        _dbContext.Entry(entry).State = EntityState.Detached;
        if (mold is not null)
        {
            _dbContext.Entry(mold).State = EntityState.Detached;
        }
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
