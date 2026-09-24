namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/vendors. Not accepted (and ignored by model binding if sent): vendorId, vendorCode
/// (issued from the VENDOR document sequence), isActive (a new vendor is always active), rowVersion and the audit
/// columns. Normalization and validation are VendorService's job.
/// </summary>
public class CreateVendorRequest
{
    public string VendorName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? ContactPerson { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
}
