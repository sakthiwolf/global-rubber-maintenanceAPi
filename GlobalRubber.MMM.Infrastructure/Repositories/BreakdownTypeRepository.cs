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

public sealed class BreakdownTypeRepository : IBreakdownTypeRepository
{
    private const string ConcurrencyMessage = "The breakdown type was modified by another user. Refresh the breakdown type and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public BreakdownTypeRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<BreakdownType> Items, int TotalCount)> GetAllAsync(
        BreakdownTypeListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.BreakdownTypes.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(t => t.IsActive == isActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var lowered = search.ToLower();
            query = query.Where(t => t.BreakdownTypeCode.ToLower().Contains(lowered) || t.BreakdownTypeName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(t => t.BreakdownTypeName).ThenBy(t => t.BreakdownTypeId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<BreakdownType?> GetByIdAsync(int breakdownTypeId, CancellationToken cancellationToken) =>
        _dbContext.BreakdownTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.BreakdownTypeId == breakdownTypeId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeBreakdownTypeId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();

        return _dbContext.BreakdownTypes.AsNoTracking()
            .AnyAsync(t => t.IsActive && t.BreakdownTypeName.ToLower() == lowered && t.BreakdownTypeId != excludeBreakdownTypeId, cancellationToken);
    }

    public async Task<BreakdownType> AddAsync(BreakdownType breakdownType, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(breakdownType).State = EntityState.Detached;

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                breakdownType.BreakdownTypeCode = await _documentSequence.NextCodeAsync(DocumentTypes.BreakdownType, cancellationToken);
                _dbContext.BreakdownTypes.Add(breakdownType);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(breakdownType).State = EntityState.Detached;
            throw new ConflictException("The breakdown type code could not be issued because it already exists. Please try again.");
        }

        _dbContext.Entry(breakdownType).State = EntityState.Detached;
        return breakdownType;
    }

    public async Task<BreakdownType> UpdateAsync(BreakdownType breakdownType, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        var stub = new BreakdownType
        {
            BreakdownTypeId = breakdownType.BreakdownTypeId,
            RowVersion = originalRowVersion,
            BreakdownTypeName = breakdownType.BreakdownTypeName,
            UpdatedAt = breakdownType.UpdatedAt,
            UpdatedBy = breakdownType.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(t => t.BreakdownTypeName).IsModified = true;
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

        breakdownType.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return breakdownType;
    }

    public async Task<BreakdownType> DeactivateAsync(BreakdownType breakdownType, CancellationToken cancellationToken)
    {
        var stub = new BreakdownType
        {
            BreakdownTypeId = breakdownType.BreakdownTypeId,
            RowVersion = breakdownType.RowVersion,
            IsActive = breakdownType.IsActive,
            UpdatedAt = breakdownType.UpdatedAt,
            UpdatedBy = breakdownType.UpdatedBy,
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

        breakdownType.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return breakdownType;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}