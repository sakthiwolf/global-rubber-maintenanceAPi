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

public sealed class MaintenanceTypeRepository : IMaintenanceTypeRepository
{
    private const string ConcurrencyMessage = "The maintenance type was modified by another user. Refresh the maintenance type and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MaintenanceTypeRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<MaintenanceType> Items, int TotalCount)> GetAllAsync(
        MaintenanceTypeListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.MaintenanceTypes.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(t => t.IsActive == isActive);
        }

        var appliesTo = request.AppliesTo?.Trim();
        if (!string.IsNullOrEmpty(appliesTo))
        {
            query = query.Where(t => t.AppliesTo == appliesTo);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(t => t.MaintenanceTypeCode.ToLower().Contains(lowered) || t.MaintenanceTypeName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(t => t.MaintenanceTypeName).ThenBy(t => t.MaintenanceTypeId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<MaintenanceType?> GetByIdAsync(int maintenanceTypeId, CancellationToken cancellationToken) =>
        _dbContext.MaintenanceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.MaintenanceTypeId == maintenanceTypeId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeMaintenanceTypeId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return _dbContext.MaintenanceTypes.AsNoTracking()
            .AnyAsync(t => t.IsActive && t.MaintenanceTypeName.ToLower() == lowered && t.MaintenanceTypeId != excludeMaintenanceTypeId, cancellationToken);
    }

    public async Task<MaintenanceType> AddAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(maintenanceType).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                maintenanceType.MaintenanceTypeCode = await _documentSequence.NextCodeAsync(DocumentTypes.MaintenanceType, cancellationToken);
                _dbContext.MaintenanceTypes.Add(maintenanceType);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(maintenanceType).State = EntityState.Detached;

            // The code is the only unique constraint and it is issued by the sequence, not the client, so a collision
            // means the sequence and the table disagree.
            throw new ConflictException("The maintenance type code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version but stop tracking: a later write in the same scope attaches its own stub.
        _dbContext.Entry(maintenanceType).State = EntityState.Detached;

        return maintenanceType;
    }

    public async Task<MaintenanceType> UpdateAsync(MaintenanceType maintenanceType, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): the row version the
        // CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause - the optimistic-concurrency check.
        var stub = new MaintenanceType
        {
            MaintenanceTypeId = maintenanceType.MaintenanceTypeId,
            RowVersion = originalRowVersion,
            MaintenanceTypeName = maintenanceType.MaintenanceTypeName,
            AppliesTo = maintenanceType.AppliesTo,
            UpdatedAt = maintenanceType.UpdatedAt,
            UpdatedBy = maintenanceType.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(t => t.MaintenanceTypeName).IsModified = true;
        entry.Property(t => t.AppliesTo).IsModified = true;
        entry.Property(t => t.UpdatedAt).IsModified = true;
        entry.Property(t => t.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        maintenanceType.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return maintenanceType;
    }

    public async Task<MaintenanceType> DeactivateAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the maintenance type was loaded with; the ONLY columns marked modified are
        // is_active, updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new MaintenanceType
        {
            MaintenanceTypeId = maintenanceType.MaintenanceTypeId,
            RowVersion = maintenanceType.RowVersion,
            IsActive = maintenanceType.IsActive,
            UpdatedAt = maintenanceType.UpdatedAt,
            UpdatedBy = maintenanceType.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(t => t.IsActive).IsModified = true;
        entry.Property(t => t.UpdatedAt).IsModified = true;
        entry.Property(t => t.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        maintenanceType.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return maintenanceType;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
