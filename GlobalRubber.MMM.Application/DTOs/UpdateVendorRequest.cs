namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/vendors/{id} - a full replacement of the editable fields plus the row version the caller
/// last read (409 if the vendor changed since). Not accepted: vendorId, vendorCode (immutable), isActive (deactivation
/// is DELETE), audit columns.
/// </summary>
public sealed class UpdateVendorRequest : CreateVendorRequest
{
    /// <summary>Base64 row_version exactly as returned by VendorDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
