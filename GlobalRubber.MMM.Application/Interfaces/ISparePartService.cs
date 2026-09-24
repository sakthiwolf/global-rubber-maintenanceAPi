using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface ISparePartService
{
    Task<PagedResult<SparePartDto>> GetAllAsync(SparePartListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No spare part exists with the given id.</exception>
    Task<SparePartDto> GetByIdAsync(int sparePartId, CancellationToken cancellationToken);

    /// <summary>Creates an active spare part with a code from the SPARE_PART sequence. Writes SparePartCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the machine / vendor is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The linked machine or supplier does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active spare part has the same name.</exception>
    Task<SparePartDto> CreateAsync(CreateSparePartRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the editable fields, guarded by the caller's row version. A machine / vendor kept unchanged may be inactive;
    /// a newly selected one must be active. Writes SparePartUpdated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The spare part, or a newly selected machine / vendor, does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, the row version is missing/invalid, or a newly selected machine / vendor is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Duplicate name, or the spare part was modified after it was loaded.</exception>
    Task<SparePartDto> UpdateAsync(int sparePartId, UpdateSparePartRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes SparePartDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No spare part exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">It is already inactive, or was modified after it was loaded.</exception>
    Task<SparePartDto> DeactivateAsync(int sparePartId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
