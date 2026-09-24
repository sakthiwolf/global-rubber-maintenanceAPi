using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IMaintenanceTypeService
{
    Task<PagedResult<MaintenanceTypeDto>> GetAllAsync(MaintenanceTypeListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No maintenance type exists with the given id.</exception>
    Task<MaintenanceTypeDto> GetByIdAsync(int maintenanceTypeId, CancellationToken cancellationToken);

    /// <summary>Creates an active maintenance type with a code from the MAINTENANCE_TYPE sequence. Writes MaintenanceTypeCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active maintenance type has the same name.</exception>
    Task<MaintenanceTypeDto> CreateAsync(CreateMaintenanceTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Updates name and applies-to, guarded by the caller's row version. Writes MaintenanceTypeUpdated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No maintenance type exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the row version is missing/invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active maintenance type has the name, or it was modified after it was loaded.</exception>
    Task<MaintenanceTypeDto> UpdateAsync(int maintenanceTypeId, UpdateMaintenanceTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes MaintenanceTypeDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No maintenance type exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">It is already inactive, or was modified after it was loaded.</exception>
    Task<MaintenanceTypeDto> DeactivateAsync(int maintenanceTypeId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
