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

public sealed class MoldRepository : IMoldRepository
{
    private const string ConcurrencyMessage = "The mold was modified by another user. Refresh the mold and try again.";
    private const string SerialNumberIndex = "UX_mold_master_serial_number";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MoldRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Mold> Items, int TotalCount)> GetAllAsync(
        MoldListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Molds.AsNoTracking();

        var status = request.Status?.Trim();
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(m => m.Status == status);
        }

        if (request.ProductId is { } productId)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        var lifeState = request.LifeState?.Trim();
        if (!string.IsNullOrEmpty(lifeState))
        {
            query = query.Where(m => m.LifeState == lifeState);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(m => m.MoldCode.ToLower().Contains(lowered) || m.MoldName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(m => m.MoldName).ThenBy(m => m.MoldId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Include(m => m.Product)
            .Include(m => m.ResponsibleEmployee)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Mold?> GetByIdAsync(int moldId, CancellationToken cancellationToken) =>
        _dbContext.Molds
            .AsNoTracking()
            .Include(m => m.Product)
            .Include(m => m.ResponsibleEmployee)
            .FirstOrDefaultAsync(m => m.MoldId == moldId, cancellationToken);

    public Task<bool> ExistsNonRetiredByNameAsync(string moldName, int? excludeMoldId, CancellationToken cancellationToken)
    {
        var lowered = moldName.ToLower();

        return _dbContext.Molds.AsNoTracking()
            .AnyAsync(m => m.Status != MoldStatus.Retired && m.MoldName.ToLower() == lowered && m.MoldId != excludeMoldId, cancellationToken);
    }

    public Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMoldId, CancellationToken cancellationToken)
    {
        var lowered = serialNumber.ToLower();

        return _dbContext.Molds.AsNoTracking()
            .AnyAsync(m => m.SerialNumber != null && m.SerialNumber.ToLower() == lowered && m.MoldId != excludeMoldId, cancellationToken);
    }

    public async Task<Mold> AddAsync(Mold mold, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(mold).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                mold.MoldCode = await _documentSequence.NextCodeAsync(DocumentTypes.Mold, cancellationToken);
                _dbContext.Molds.Add(mold);
                await _dbContext.SaveChangesAsync(cancellationToken); // also reads back the computed life_state

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var serialNumberTaken))
        {
            _dbContext.Entry(mold).State = EntityState.Detached;

            // The service checks first; this is the race where another save got in between. The index name is only
            // inspected here, never sent to the client.
            throw new ConflictException(serialNumberTaken
                ? $"A mold with serial number '{mold.SerialNumber}' already exists."
                : "The mold code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version/life_state but stop tracking: a later write attaches its own stub.
        _dbContext.Entry(mold).State = EntityState.Detached;

        return mold;
    }

    public async Task<Mold> UpdateAsync(Mold mold, byte[] originalRowVersion, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside the
        // execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        Mold? stub = null;

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                if (stub is not null) _dbContext.Entry(stub).State = EntityState.Detached; // a retry starts clean

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

                // Attach a stub (key + the caller's row version + only the values this operation writes): no navigations,
                // and the row version the CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause.
                stub = new Mold
                {
                    MoldId = mold.MoldId,
                    RowVersion = originalRowVersion,
                    MoldName = mold.MoldName,
                    ProductId = mold.ProductId,
                    MoldType = mold.MoldType,
                    CavityCount = mold.CavityCount,
                    Manufacturer = mold.Manufacturer,
                    SerialNumber = mold.SerialNumber,
                    Location = mold.Location,
                    StorageLocation = mold.StorageLocation,
                    CommissionDate = mold.CommissionDate,
                    MaximumShots = mold.MaximumShots,
                    WarningShots = mold.WarningShots,
                    ReplacementShots = mold.ReplacementShots,
                    MaintenanceFrequencyShots = mold.MaintenanceFrequencyShots,
                    PmWarningShots = mold.PmWarningShots,
                    CurrentUsageShots = mold.CurrentUsageShots,
                    ResponsibleEmployeeId = mold.ResponsibleEmployeeId,
                    Status = mold.Status,
                    Remarks = mold.Remarks,
                    UpdatedAt = mold.UpdatedAt,
                    UpdatedBy = mold.UpdatedBy,
                };

                var entry = _dbContext.Attach(stub);
                foreach (var property in new[]
                {
                    nameof(Mold.MoldName), nameof(Mold.ProductId), nameof(Mold.MoldType), nameof(Mold.CavityCount), nameof(Mold.Manufacturer),
                    nameof(Mold.SerialNumber), nameof(Mold.Location), nameof(Mold.StorageLocation), nameof(Mold.CommissionDate),
                    nameof(Mold.MaximumShots), nameof(Mold.WarningShots), nameof(Mold.ReplacementShots), nameof(Mold.MaintenanceFrequencyShots),
                    nameof(Mold.PmWarningShots), nameof(Mold.CurrentUsageShots), nameof(Mold.ResponsibleEmployeeId), nameof(Mold.Status),
                    nameof(Mold.Remarks), nameof(Mold.UpdatedAt), nameof(Mold.UpdatedBy),
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
                catch (DbUpdateException ex) when (IsUniqueViolation(ex, out _))
                {
                    // mold_code is never written here, so the only unique index an update can hit is the serial number's.
                    throw new ConflictException($"A mold with serial number '{mold.SerialNumber}' already exists.");
                }

                mold.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
                mold.LifeState = stub.LifeState;   // recomputed by SQL Server on save

                // The caller's full mold (it carries the system-managed cycle start the stub does not write), evaluated
                // while the updated row is exclusively locked by this transaction.
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
            if (stub is not null) _dbContext.Entry(stub).State = EntityState.Detached;
        }

        return mold;
    }

    public async Task<Mold> RetireAsync(Mold mold, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the mold was loaded with; the ONLY columns marked modified are status, updated_at
        // and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Mold
        {
            MoldId = mold.MoldId,
            RowVersion = mold.RowVersion,
            Status = mold.Status,
            UpdatedAt = mold.UpdatedAt,
            UpdatedBy = mold.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(m => m.Status).IsModified = true;
        entry.Property(m => m.UpdatedAt).IsModified = true;
        entry.Property(m => m.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        mold.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return mold;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex, out bool serialNumberTaken)
    {
        serialNumberTaken = false;
        if (ex.InnerException is not SqlException { Number: 2601 or 2627 } sql)
        {
            return false;
        }

        serialNumberTaken = sql.Message.Contains(SerialNumberIndex, StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
