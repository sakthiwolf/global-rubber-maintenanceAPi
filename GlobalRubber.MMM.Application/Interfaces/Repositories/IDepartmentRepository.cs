using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IDepartmentRepository
{
    Task<(IReadOnlyList<Department> Items, int TotalCount)> GetAllAsync(
        DepartmentListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a department without tracking; it carries the row_version it was read with.</summary>
    Task<Department?> GetByIdAsync(int departmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another ACTIVE department already has this name (case-insensitive). <paramref name="excludeDepartmentId"/>
    /// leaves one department out (the one being edited). Inactive departments never count.
    /// </summary>
    Task<bool> ExistsActiveByNameAsync(string departmentName, int? excludeDepartmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the department and assigns its DepartmentCode from the DEPARTMENT document sequence, both in ONE
    /// transaction: a failed insert rolls the sequence increment back, so no number is burned.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    Task<Department> AddAsync(Department department, CancellationToken cancellationToken);

    /// <summary>
    /// Saves DepartmentName, Remarks, UpdatedAt and UpdatedBy of a department loaded through
    /// <see cref="GetByIdAsync"/>. Nothing else is written - never DepartmentCode, IsActive or the creation columns.
    /// <paramref name="originalRowVersion"/> (the version the caller last read) is the optimistic-concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    Task<Department> UpdateAsync(Department department, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a department loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Department> DeactivateAsync(Department department, CancellationToken cancellationToken);
}
