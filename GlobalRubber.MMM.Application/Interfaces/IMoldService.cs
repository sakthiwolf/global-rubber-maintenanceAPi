using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IMoldService
{
    Task<PagedResult<MoldDto>> GetAllAsync(MoldListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No mold exists with the given id.</exception>
    Task<MoldDto> GetByIdAsync(int moldId, CancellationToken cancellationToken);

    /// <summary>Creates a mold with 0 usage and a code issued from the MOLD document sequence. Writes MoldCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the product / person is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The product or responsible person does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another non-retired mold has the name, or the serial number is taken.</exception>
    Task<MoldDto> CreateAsync(CreateMoldRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the editable fields (incl. status and a usage correction), guarded by the caller's row version. A product /
    /// person kept unchanged may be inactive; a newly selected one must be active. Writes MoldUpdated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The mold, or a newly selected product / person, does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, the row version is missing/invalid, or a newly selected product / person is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Duplicate name / serial number, or the mold was modified after it was loaded.</exception>
    Task<MoldDto> UpdateAsync(int moldId, UpdateMoldRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// "Deactivates" a mold the way the mold table models it: Status = Retired (there is no is_active column). The row is
    /// kept. Writes MoldDeactivated.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No mold exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The mold is already retired, or was modified after it was loaded.</exception>
    Task<MoldDto> DeactivateAsync(int moldId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
