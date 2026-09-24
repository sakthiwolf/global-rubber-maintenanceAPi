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

public sealed class VendorRepository : IVendorRepository
{
    private const string ConcurrencyMessage = "The vendor was modified by another user. Refresh the vendor and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public VendorRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Vendor> Items, int TotalCount)> GetAllAsync(
        VendorListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Vendors.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(v => v.IsActive == isActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(v => v.VendorCode.ToLower().Contains(lowered) || v.VendorName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(v => v.VendorName).ThenBy(v => v.VendorId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Vendor?> GetByIdAsync(int vendorId, CancellationToken cancellationToken) =>
        _dbContext.Vendors
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VendorId == vendorId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string vendorName, int? excludeVendorId, CancellationToken cancellationToken)
    {
        var lowered = vendorName.ToLower();

        return _dbContext.Vendors.AsNoTracking()
            .AnyAsync(v => v.IsActive && v.VendorName.ToLower() == lowered && v.VendorId != excludeVendorId, cancellationToken);
    }

    public async Task<Vendor> AddAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(vendor).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                vendor.VendorCode = await _documentSequence.NextCodeAsync(DocumentTypes.Vendor, cancellationToken);
                _dbContext.Vendors.Add(vendor);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(vendor).State = EntityState.Detached;

            // vendor_code is the only unique constraint and it is issued by the sequence, not the client, so a collision
            // means the sequence and the table disagree.
            throw new ConflictException("The vendor code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version but stop tracking: a later write in the same scope attaches its own stub.
        _dbContext.Entry(vendor).State = EntityState.Detached;

        return vendor;
    }

    public async Task<Vendor> UpdateAsync(Vendor vendor, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): the row version the
        // CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause - the optimistic-concurrency check.
        var stub = new Vendor
        {
            VendorId = vendor.VendorId,
            RowVersion = originalRowVersion,
            VendorName = vendor.VendorName,
            Category = vendor.Category,
            ContactPerson = vendor.ContactPerson,
            Mobile = vendor.Mobile,
            Email = vendor.Email,
            Address = vendor.Address,
            UpdatedAt = vendor.UpdatedAt,
            UpdatedBy = vendor.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(v => v.VendorName).IsModified = true;
        entry.Property(v => v.Category).IsModified = true;
        entry.Property(v => v.ContactPerson).IsModified = true;
        entry.Property(v => v.Mobile).IsModified = true;
        entry.Property(v => v.Email).IsModified = true;
        entry.Property(v => v.Address).IsModified = true;
        entry.Property(v => v.UpdatedAt).IsModified = true;
        entry.Property(v => v.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        vendor.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return vendor;
    }

    public async Task<Vendor> DeactivateAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the vendor was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Vendor
        {
            VendorId = vendor.VendorId,
            RowVersion = vendor.RowVersion,
            IsActive = vendor.IsActive,
            UpdatedAt = vendor.UpdatedAt,
            UpdatedBy = vendor.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(v => v.IsActive).IsModified = true;
        entry.Property(v => v.UpdatedAt).IsModified = true;
        entry.Property(v => v.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        vendor.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return vendor;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
