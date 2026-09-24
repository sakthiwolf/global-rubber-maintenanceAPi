namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/products/{id} - a full replacement of the editable fields plus the row version the
/// caller last read (409 if the product changed since). Not accepted: productId, productCode (immutable), isActive
/// (deactivation is DELETE), audit columns.
/// </summary>
public sealed class UpdateProductRequest : CreateProductRequest
{
    /// <summary>Base64 row_version exactly as returned by ProductDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
