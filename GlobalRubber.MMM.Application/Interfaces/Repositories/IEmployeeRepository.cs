using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IEmployeeRepository
{
    /// <summary>A page of employees (name-ordered) with their Department loaded.</summary>
    Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetAllAsync(
        EmployeeListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads an employee (with its Department) without tracking; it carries the row_version it was read with.</summary>
    Task<Employee?> GetByIdAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the employee and assigns its EmployeeCode from the EMPLOYEE document sequence, both in ONE transaction:
    /// a failed insert rolls the sequence increment back, so no number is burned. The Department navigation is not
    /// saved - only DepartmentId.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken);

    /// <summary>
    /// Saves EmployeeName, Designation, DepartmentId, Mobile, Email, UpdatedAt and UpdatedBy of an employee loaded through
    /// <see cref="GetByIdAsync"/>. Never EmployeeCode, IsActive or the creation columns. <paramref name="originalRowVersion"/>
    /// (the version the caller last read) is the optimistic-concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    Task<Employee> UpdateAsync(Employee employee, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of an employee loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Employee> DeactivateAsync(Employee employee, CancellationToken cancellationToken);
}
