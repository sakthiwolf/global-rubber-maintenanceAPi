using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IMaintenanceChecklistService
{
    Task<PagedResult<MaintenanceChecklistDto>> GetAllAsync(MaintenanceChecklistListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No checklist exists with the given id.</exception>
    Task<MaintenanceChecklistDto> GetByIdAsync(int checklistId, CancellationToken cancellationToken);

    /// <summary>The ACTIVE machines a Machine checklist can be assigned to (dropdown data).</summary>
    Task<MaintenanceChecklistLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>Creates an active checklist with a code from the MAINTENANCE_CHECKLIST sequence. Writes ChecklistCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active checklist has the same name.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The machine does not exist.</exception>
    Task<MaintenanceChecklistDto> CreateAsync(CreateMaintenanceChecklistRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Updates name, applies-to, frequency, machine and the item list, guarded by the caller's row version. Writes ChecklistUpdated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No checklist exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the row version is missing/invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active checklist has the name, or it was modified after it was loaded.</exception>
    Task<MaintenanceChecklistDto> UpdateAsync(int checklistId, UpdateMaintenanceChecklistRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The header and items are kept. Writes ChecklistDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No checklist exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">It is already inactive, or was modified after it was loaded.</exception>
    Task<MaintenanceChecklistDto> DeactivateAsync(int checklistId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
