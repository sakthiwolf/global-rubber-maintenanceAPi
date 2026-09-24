namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/products. Not accepted (and ignored by model binding if sent): productId, productCode
/// (issued from the PRODUCT document sequence), isActive (a new product is always active), rowVersion and the audit
/// columns. Normalization and validation are ProductService's job.
/// </summary>
public class CreateProductRequest
{
    public string ProductName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string UnitOfMeasure { get; init; } = string.Empty;
    public decimal? StandardCycleTimeSec { get; init; }
    public string? Remarks { get; init; }
}
