using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IEmployeeService
{
    Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No employee exists with the given id.</exception>
    Task<EmployeeDto> GetByIdAsync(int employeeId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an active employee with a code issued from the EMPLOYEE document sequence. <paramref name="actingUserId"/> /
    /// <paramref name="ipAddress"/> come from the controller and feed CreatedBy and the EmployeeCreated audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the department is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The department does not exist.</exception>
    Task<EmployeeDto> CreateAsync(
        CreateEmployeeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the editable fields, guarded by the caller's row version. A department that is kept unchanged may be
    /// inactive; a newly selected one must be active. Writes EmployeeUpdated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The employee, or the newly selected department, does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, the row version is missing/invalid, or the new department is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The employee was modified by someone else after it was loaded.</exception>
    Task<EmployeeDto> UpdateAsync(
        int employeeId, UpdateEmployeeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes EmployeeDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No employee exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The employee is already inactive, or was modified after it was loaded.</exception>
    Task<EmployeeDto> DeactivateAsync(
        int employeeId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
