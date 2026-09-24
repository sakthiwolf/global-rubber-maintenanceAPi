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

public sealed class EmployeeRepository : IEmployeeRepository
{
    private const string ConcurrencyMessage = "The employee was modified by another user. Refresh the employee and try again.";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public EmployeeRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetAllAsync(
        EmployeeListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Employees.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(e => e.IsActive == isActive);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(e => e.EmployeeCode.ToLower().Contains(lowered) || e.EmployeeName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(e => e.EmployeeName).ThenBy(e => e.EmployeeId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Include(e => e.Department)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Employee?> GetByIdAsync(int employeeId, CancellationToken cancellationToken) =>
        _dbContext.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);

    public async Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(employee).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                employee.EmployeeCode = await _documentSequence.NextCodeAsync(DocumentTypes.Employee, cancellationToken);
                _dbContext.Employees.Add(employee);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(employee).State = EntityState.Detached;

            // employee_code is the only unique constraint and it is issued by the sequence, not the client, so a
            // collision means the sequence and the table disagree.
            throw new ConflictException("The employee code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version but stop tracking: a later write in the same scope attaches its own stub.
        _dbContext.Entry(employee).State = EntityState.Detached;

        return employee;
    }

    public async Task<Employee> UpdateAsync(Employee employee, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): no Department
        // navigation, and the row version the CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause.
        var stub = new Employee
        {
            EmployeeId = employee.EmployeeId,
            RowVersion = originalRowVersion,
            EmployeeName = employee.EmployeeName,
            Designation = employee.Designation,
            DepartmentId = employee.DepartmentId,
            Mobile = employee.Mobile,
            Email = employee.Email,
            UpdatedAt = employee.UpdatedAt,
            UpdatedBy = employee.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(e => e.EmployeeName).IsModified = true;
        entry.Property(e => e.Designation).IsModified = true;
        entry.Property(e => e.DepartmentId).IsModified = true;
        entry.Property(e => e.Mobile).IsModified = true;
        entry.Property(e => e.Email).IsModified = true;
        entry.Property(e => e.UpdatedAt).IsModified = true;
        entry.Property(e => e.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        employee.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return employee;
    }

    public async Task<Employee> DeactivateAsync(Employee employee, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the employee was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Employee
        {
            EmployeeId = employee.EmployeeId,
            RowVersion = employee.RowVersion,
            IsActive = employee.IsActive,
            UpdatedAt = employee.UpdatedAt,
            UpdatedBy = employee.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(e => e.IsActive).IsModified = true;
        entry.Property(e => e.UpdatedAt).IsModified = true;
        entry.Property(e => e.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        employee.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return employee;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
