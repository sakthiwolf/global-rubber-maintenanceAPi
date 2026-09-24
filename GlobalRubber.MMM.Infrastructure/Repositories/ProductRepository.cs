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

public sealed class ProductRepository : IProductRepository
{
    private const string ConcurrencyMessage = "The product was modified by another user. Refresh the product and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public ProductRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> GetAllAsync(
        ProductListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Products.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(p => p.IsActive == isActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(p => p.ProductCode.ToLower().Contains(lowered) || p.ProductName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(p => p.ProductName).ThenBy(p => p.ProductId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken) =>
        _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductId == productId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string productName, int? excludeProductId, CancellationToken cancellationToken)
    {
        var lowered = productName.ToLower();

        return _dbContext.Products.AsNoTracking()
            .AnyAsync(p => p.IsActive && p.ProductName.ToLower() == lowered && p.ProductId != excludeProductId, cancellationToken);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(product).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                product.ProductCode = await _documentSequence.NextCodeAsync(DocumentTypes.Product, cancellationToken);
                _dbContext.Products.Add(product);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(product).State = EntityState.Detached;

            // product_code is the only unique constraint and it is issued by the sequence, not the client, so a collision
            // means the sequence and the table disagree.
            throw new ConflictException("The product code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version but stop tracking: a later write in the same scope attaches its own stub.
        _dbContext.Entry(product).State = EntityState.Detached;

        return product;
    }

    public async Task<Product> UpdateAsync(Product product, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): the row version the
        // CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause - the optimistic-concurrency check.
        var stub = new Product
        {
            ProductId = product.ProductId,
            RowVersion = originalRowVersion,
            ProductName = product.ProductName,
            Category = product.Category,
            UnitOfMeasure = product.UnitOfMeasure,
            StandardCycleTimeSec = product.StandardCycleTimeSec,
            Remarks = product.Remarks,
            UpdatedAt = product.UpdatedAt,
            UpdatedBy = product.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(p => p.ProductName).IsModified = true;
        entry.Property(p => p.Category).IsModified = true;
        entry.Property(p => p.UnitOfMeasure).IsModified = true;
        entry.Property(p => p.StandardCycleTimeSec).IsModified = true;
        entry.Property(p => p.Remarks).IsModified = true;
        entry.Property(p => p.UpdatedAt).IsModified = true;
        entry.Property(p => p.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        product.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return product;
    }

    public async Task<Product> DeactivateAsync(Product product, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the product was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Product
        {
            ProductId = product.ProductId,
            RowVersion = product.RowVersion,
            IsActive = product.IsActive,
            UpdatedAt = product.UpdatedAt,
            UpdatedBy = product.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(p => p.IsActive).IsModified = true;
        entry.Property(p => p.UpdatedAt).IsModified = true;
        entry.Property(p => p.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        product.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return product;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
