namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Spare part information returned to API callers (masters.spare_part_master joined to its linked machine and supplier).
/// The joined names let pages show them without needing Machine/Vendor view permission.
/// </summary>
public sealed class SparePartDto
{
    public int SparePartId { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public int? MachineId { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }

    /// <summary>Null when no machine is linked.</summary>
    public bool? MachineIsActive { get; init; }

    public string? PartNumber { get; init; }
    public string Unit { get; init; } = string.Empty;
    public int MinimumStock { get; init; }
    public int CurrentStock { get; init; }
    public int? VendorId { get; init; }
    public string? VendorName { get; init; }

    /// <summary>Null when no supplier is set.</summary>
    public bool? VendorIsActive { get; init; }

    public string? StoreLocation { get; init; }
    public decimal? UnitCost { get; init; }

    /// <summary>Out of Stock / Low Stock / Available - the persisted computed column stock_status.</summary>
    public string StockStatus { get; init; } = string.Empty;

    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the spare part changed in between (including a
    /// stock change by a usage transaction), the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
