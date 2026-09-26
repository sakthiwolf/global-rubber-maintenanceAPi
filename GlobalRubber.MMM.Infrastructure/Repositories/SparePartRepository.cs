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

public sealed class SparePartRepository : ISparePartRepository
{
    private const string ConcurrencyMessage = "The spare part was modified by another user. Refresh the spare part and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public SparePartRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetAllAsync(
        SparePartListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.SpareParts.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(s => s.IsActive == isActive);
        }

        var stockStatus = request.StockStatus?.Trim();
        if (!string.IsNullOrEmpty(stockStatus))
        {
            query = query.Where(s => s.StockStatus == stockStatus);
        }

        if (request.MachineId is { } machineId)
        {
            query = query.Where(s => s.MachineId == machineId);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(s => s.SparePartCode.ToLower().Contains(lowered) || s.SparePartName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(s => s.SparePartName).ThenBy(s => s.SparePartId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Include(s => s.Machine)
            .Include(s => s.Vendor)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<SparePart?> GetByIdAsync(int sparePartId, CancellationToken cancellationToken) =>
        _dbContext.SpareParts
            .AsNoTracking()
            .Include(s => s.Machine)
            .Include(s => s.Vendor)
            .FirstOrDefaultAsync(s => s.SparePartId == sparePartId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeSparePartId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return _dbContext.SpareParts.AsNoTracking()
            .AnyAsync(s => s.IsActive && s.SparePartName.ToLower() == lowered && s.SparePartId != excludeSparePartId, cancellationToken);
    }

    public async Task<SparePart> AddAsync(SparePart sparePart, SparePartStockTransaction openingEntry, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(sparePart).State = EntityState.Detached; // a retry must not see the previous attempt's tracking
                _dbContext.Entry(openingEntry).State = EntityState.Detached;

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                sparePart.SparePartCode = await _documentSequence.NextCodeAsync(DocumentTypes.SparePart, cancellationToken);
                _dbContext.SpareParts.Add(sparePart);
                await _dbContext.SaveChangesAsync(cancellationToken); // also reads back the computed stock_status

                // The stock ledger's first row for the part (migration 016).
                openingEntry.SparePartId = sparePart.SparePartId;
                openingEntry.ReferenceId = sparePart.SparePartId;
                openingEntry.ReferenceNo = sparePart.SparePartCode;
                _dbContext.SparePartStockTransactions.Add(openingEntry);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(sparePart).State = EntityState.Detached;
            _dbContext.Entry(openingEntry).State = EntityState.Detached;

            // spare_part_code is the table's only unique key: the sequence and existing data disagree.
            throw new ConflictException("The spare part code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version/stock_status but stop tracking: a later write attaches its own stub.
        _dbContext.Entry(sparePart).State = EntityState.Detached;
        _dbContext.Entry(openingEntry).State = EntityState.Detached;

        return sparePart;
    }

    public async Task<SparePart> UpdateAsync(
        SparePart sparePart, byte[] originalRowVersion, SparePartStockTransaction? stockEntry, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside the
        // execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        SparePart? stub = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                if (stub is not null) _dbContext.Entry(stub).State = EntityState.Detached; // a retry starts clean
                if (stockEntry is not null) _dbContext.Entry(stockEntry).State = EntityState.Detached;

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

                // Attach a stub (key + the caller's row version + only the values this operation writes): no navigations,
                // and the row version the CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause.
                stub = new SparePart
                {
                    SparePartId = sparePart.SparePartId,
                    RowVersion = originalRowVersion,
                    SparePartName = sparePart.SparePartName,
                    Category = sparePart.Category,
                    MachineId = sparePart.MachineId,
                    PartNumber = sparePart.PartNumber,
                    Unit = sparePart.Unit,
                    MinimumStock = sparePart.MinimumStock,
                    CurrentStock = sparePart.CurrentStock,
                    VendorId = sparePart.VendorId,
                    StoreLocation = sparePart.StoreLocation,
                    UnitCost = sparePart.UnitCost,
                    UpdatedAt = sparePart.UpdatedAt,
                    UpdatedBy = sparePart.UpdatedBy,
                };

                var entry = _dbContext.Attach(stub);
                foreach (var property in new[]
                {
                    nameof(SparePart.SparePartName), nameof(SparePart.Category), nameof(SparePart.MachineId), nameof(SparePart.PartNumber),
                    nameof(SparePart.Unit), nameof(SparePart.MinimumStock), nameof(SparePart.CurrentStock), nameof(SparePart.VendorId),
                    nameof(SparePart.StoreLocation), nameof(SparePart.UnitCost), nameof(SparePart.UpdatedAt), nameof(SparePart.UpdatedBy),
                })
                {
                    entry.Property(property).IsModified = true;
                }

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new ConflictException(ConcurrencyMessage);
                }

                // The stock changed on the master: its Adjustment ledger row, same transaction (migration 016).
                if (stockEntry is not null)
                {
                    stockEntry.SparePartId = sparePart.SparePartId;
                    _dbContext.SparePartStockTransactions.Add(stockEntry);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        finally
        {
            if (stub is not null) _dbContext.Entry(stub).State = EntityState.Detached;
            if (stockEntry is not null) _dbContext.Entry(stockEntry).State = EntityState.Detached;
        }

        sparePart.RowVersion = stub!.RowVersion;   // refreshed by SQL Server on save
        sparePart.StockStatus = stub.StockStatus; // recomputed by SQL Server on save
        return sparePart;
    }

    public async Task<SparePart> DeactivateAsync(SparePart sparePart, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the spare part was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch stock or anything else, and no DELETE is ever issued.
        var stub = new SparePart
        {
            SparePartId = sparePart.SparePartId,
            RowVersion = sparePart.RowVersion,
            IsActive = sparePart.IsActive,
            UpdatedAt = sparePart.UpdatedAt,
            UpdatedBy = sparePart.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(s => s.IsActive).IsModified = true;
        entry.Property(s => s.UpdatedAt).IsModified = true;
        entry.Property(s => s.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        sparePart.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return sparePart;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
