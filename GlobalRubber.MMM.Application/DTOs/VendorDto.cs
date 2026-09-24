namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Vendor information returned to API callers (masters.vendor_master).</summary>
public sealed class VendorDto
{
    public int VendorId { get; init; }
    public string VendorCode { get; init; } = string.Empty;
    public string VendorName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? ContactPerson { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the vendor changed in between, the update
    /// is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
