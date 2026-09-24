namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Product information returned to API callers (masters.product_master).</summary>
public sealed class ProductDto
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string UnitOfMeasure { get; init; } = string.Empty;
    public decimal? StandardCycleTimeSec { get; init; }
    public string? Remarks { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the product changed in between, the update
    /// is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
