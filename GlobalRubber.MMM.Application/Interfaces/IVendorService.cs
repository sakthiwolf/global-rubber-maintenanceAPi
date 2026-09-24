using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IVendorService
{
    Task<PagedResult<VendorDto>> GetAllAsync(VendorListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No vendor exists with the given id.</exception>
    Task<VendorDto> GetByIdAsync(int vendorId, CancellationToken cancellationToken);

    /// <summary>Creates an active vendor with a code issued from the VENDOR document sequence. Writes VendorCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active vendor has the same name.</exception>
    Task<VendorDto> CreateAsync(CreateVendorRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Updates the editable fields, guarded by the caller's row version. Writes VendorUpdated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No vendor exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the row version is missing/invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active vendor has the name, or the vendor was modified after it was loaded.</exception>
    Task<VendorDto> UpdateAsync(int vendorId, UpdateVendorRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes VendorDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No vendor exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The vendor is already inactive, or was modified after it was loaded.</exception>
    Task<VendorDto> DeactivateAsync(int vendorId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
