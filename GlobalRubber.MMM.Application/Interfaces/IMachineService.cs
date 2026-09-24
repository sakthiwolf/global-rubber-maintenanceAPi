using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IMachineService
{
    Task<PagedResult<MachineDto>> GetAllAsync(MachineListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No machine exists with the given id.</exception>
    Task<MachineDto> GetByIdAsync(int machineId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an active, Running machine with a code issued from the MACHINE document sequence. Writes MachineCreated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the department is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The department does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active machine has the name, or the serial number is taken.</exception>
    Task<MachineDto> CreateAsync(CreateMachineRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the editable fields, guarded by the caller's row version. A department kept unchanged may be inactive; a
    /// newly selected one must be active. Writes MachineUpdated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The machine, or a newly selected department, does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, the row version is missing/invalid, or a newly selected department is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Duplicate name / serial number, or the machine was modified after it was loaded.</exception>
    Task<MachineDto> UpdateAsync(int machineId, UpdateMachineRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes MachineDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No machine exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The machine is already inactive, or was modified after it was loaded.</exception>
    Task<MachineDto> DeactivateAsync(int machineId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
