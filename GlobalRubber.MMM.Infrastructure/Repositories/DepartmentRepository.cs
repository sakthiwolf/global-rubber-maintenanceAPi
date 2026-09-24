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

public sealed class DepartmentRepository : IDepartmentRepository
{
    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public DepartmentRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Department> Items, int TotalCount)> GetAllAsync(
        DepartmentListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Departments.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(d => d.IsActive == isActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(d => d.DepartmentCode.ToLower().Contains(lowered) || d.DepartmentName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(d => d.DepartmentName).ThenBy(d => d.DepartmentId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Department?> GetByIdAsync(int departmentId, CancellationToken cancellationToken) =>
        _dbContext.Departments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DepartmentId == departmentId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string departmentName, int? excludeDepartmentId, CancellationToken cancellationToken)
    {
        var lowered = departmentName.ToLower();

        return _dbContext.Departments.AsNoTracking()
            .AnyAsync(d => d.IsActive && d.DepartmentName.ToLower() == lowered && d.DepartmentId != excludeDepartmentId, cancellationToken);
    }

    public async Task<Department> AddAsync(Department department, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run
        // inside the execution strategy. If the caller already has a transaction open (a test/verification
        // harness), join it instead of starting another.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(department).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                department.DepartmentCode = await _documentSequence.NextCodeAsync(DocumentTypes.Department, cancellationToken);
                _dbContext.Departments.Add(department);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(department).State = EntityState.Detached;

            // The transaction (if we own it) rolled back with the sequence increment. department_code is issued by
            // the sequence, not the client, so a collision means the sequence and the table disagree.
            throw new ConflictException("The department code could not be issued because it already exists. Please try again.");
        }

        // The saved instance keeps its generated id/code/row_version but must not stay tracked: a later write in the
        // same scope attaches its own stub for the same key.
        _dbContext.Entry(department).State = EntityState.Detached;

        return department;
    }

    public async Task<Department> UpdateAsync(Department department, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): the row version
        // the CALLER holds becomes the ORIGINAL value EF puts in the UPDATE's WHERE clause - the optimistic-
        // concurrency check, using the existing RowVersion configuration.
        var stub = new Department
        {
            DepartmentId = department.DepartmentId,
            RowVersion = originalRowVersion,
            DepartmentName = department.DepartmentName,
            Remarks = department.Remarks,
            UpdatedAt = department.UpdatedAt,
            UpdatedBy = department.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(d => d.DepartmentName).IsModified = true;
        entry.Property(d => d.Remarks).IsModified = true;
        entry.Property(d => d.UpdatedAt).IsModified = true;
        entry.Property(d => d.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;

            throw new ConflictException("The department was modified by another user. Refresh the department and try again.");
        }

        department.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return department;
    }

    public async Task<Department> DeactivateAsync(Department department, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the department was loaded with; the ONLY columns marked modified are
        // is_active, updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Department
        {
            DepartmentId = department.DepartmentId,
            RowVersion = department.RowVersion,
            IsActive = department.IsActive,
            UpdatedAt = department.UpdatedAt,
            UpdatedBy = department.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(d => d.IsActive).IsModified = true;
        entry.Property(d => d.UpdatedAt).IsModified = true;
        entry.Property(d => d.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;

            throw new ConflictException("The department was modified by another user. Refresh the department and try again.");
        }

        department.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return department;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
