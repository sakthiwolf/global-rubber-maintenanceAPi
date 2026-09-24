using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IVendorRepository
{
    Task<(IReadOnlyList<Vendor> Items, int TotalCount)> GetAllAsync(VendorListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a vendor without tracking; it carries the row_version it was read with.</summary>
    Task<Vendor?> GetByIdAsync(int vendorId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another ACTIVE vendor already has this name (case-insensitive). <paramref name="excludeVendorId"/> leaves
    /// one vendor out (the one being edited). Inactive vendors never count.
    /// </summary>
    Task<bool> ExistsActiveByNameAsync(string vendorName, int? excludeVendorId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the vendor and assigns its VendorCode from the VENDOR document sequence, both in ONE transaction: a failed
    /// insert rolls the sequence increment back, so no number is burned.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    Task<Vendor> AddAsync(Vendor vendor, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the editable fields plus UpdatedAt/UpdatedBy of a vendor loaded through <see cref="GetByIdAsync"/>. Never
    /// VendorCode, IsActive or the creation columns. <paramref name="originalRowVersion"/> is the concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    Task<Vendor> UpdateAsync(Vendor vendor, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a vendor loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Vendor> DeactivateAsync(Vendor vendor, CancellationToken cancellationToken);
}
