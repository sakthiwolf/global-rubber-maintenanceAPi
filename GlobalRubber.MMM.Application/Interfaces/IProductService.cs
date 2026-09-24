using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetAllAsync(ProductListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No product exists with the given id.</exception>
    Task<ProductDto> GetByIdAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Creates an active product with a code issued from the PRODUCT document sequence. Writes ProductCreated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active product has the same name.</exception>
    Task<ProductDto> CreateAsync(CreateProductRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Updates the editable fields, guarded by the caller's row version. Writes ProductUpdated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No product exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the row version is missing/invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">Another active product has the name, or the product was modified after it was loaded.</exception>
    Task<ProductDto> UpdateAsync(int productId, UpdateProductRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (IsActive = false). The row is kept. Writes ProductDeactivated.</summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No product exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The product is already inactive, or was modified after it was loaded.</exception>
    Task<ProductDto> DeactivateAsync(int productId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
