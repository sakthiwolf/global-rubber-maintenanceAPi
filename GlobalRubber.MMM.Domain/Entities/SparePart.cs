using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.spare_part_master. References Machine (optional "linked machine", one only - Q-23) and Vendor
/// (optional supplier); CreatedBy/UpdatedBy stay plain nullable ids like every mapped table. CurrentStock is owned by the
/// master (entered/edited directly - analysis 4.7, Q-06) and reduced by spare-part usage transactions. StockStatus is a
/// persisted computed column - read-only here. Referenced by transactions.spare_part_usage_transaction (not mapped yet).
/// </summary>
public class SparePart : AuditableEntity
{
    public int SparePartId { get; set; }
    public string SparePartCode { get; set; } = string.Empty;
    public string SparePartName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int? MachineId { get; set; }
    public string? PartNumber { get; set; }
    public string Unit { get; set; } = string.Empty;
    public int MinimumStock { get; set; }
    public int CurrentStock { get; set; }
    public int? VendorId { get; set; }
    public string? StoreLocation { get; set; }
    public decimal? UnitCost { get; set; }

    /// <summary>Computed by SQL Server (persisted): Out of Stock / Low Stock / Available (BR-29).</summary>
    public string StockStatus { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Machine? Machine { get; set; }
    public Vendor? Vendor { get; set; }
}
