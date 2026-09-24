using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IDepartmentService
{
    Task<PagedResult<DepartmentDto>> GetAllAsync(DepartmentListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No department exists with the given id.</exception>
    Task<DepartmentDto> GetByIdAsync(int departmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an active department with a code issued from the DEPARTMENT document sequence.
    /// <paramref name="actingUserId"/> / <paramref name="ipAddress"/> come from the controller and feed
    /// CreatedBy and the DepartmentCreated audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active department has the same name.</exception>
    Task<DepartmentDto> CreateAsync(
        CreateDepartmentRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Updates name and remarks, guarded by the caller's row version. Writes DepartmentUpdated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No department exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the row version is missing/invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active department has the name, or the department was modified after it was loaded.</exception>
    Task<DepartmentDto> UpdateAsync(
        int departmentId, UpdateDepartmentRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes DepartmentDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No department exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The department is already inactive, or was modified after it was loaded.</exception>
    Task<DepartmentDto> DeactivateAsync(
        int departmentId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
