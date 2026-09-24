using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetAllAsync(ProductListQuery query, CancellationToken cancellationToken);

    /// <summary>Loads a product without tracking; it carries the row_version it was read with.</summary>
    Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether another ACTIVE product already has this name (case-insensitive). <paramref name="excludeProductId"/> leaves
    /// one product out (the one being edited). Inactive products never count.
    /// </summary>
    Task<bool> ExistsActiveByNameAsync(string productName, int? excludeProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the product and assigns its ProductCode from the PRODUCT document sequence, both in ONE transaction: a
    /// failed insert rolls the sequence increment back, so no number is burned.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The issued code already exists.</exception>
    Task<Product> AddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the editable fields plus UpdatedAt/UpdatedBy of a product loaded through <see cref="GetByIdAsync"/>. Never
    /// ProductCode, IsActive or the creation columns. <paramref name="originalRowVersion"/> is the concurrency guard.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) since <paramref name="originalRowVersion"/>.</exception>
    Task<Product> UpdateAsync(Product product, byte[] originalRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a product loaded through
    /// <see cref="GetByIdAsync"/>, guarded by the row_version it was read with. The row is never deleted.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Modified by someone else (or removed) after it was loaded.</exception>
    Task<Product> DeactivateAsync(Product product, CancellationToken cancellationToken);
}
