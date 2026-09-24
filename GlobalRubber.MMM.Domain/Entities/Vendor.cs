using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.vendor_master. The table has no foreign key to another master (only created_by/updated_by to
/// security.user_master, kept as plain ids like every mapped table); it is referenced by masters.spare_part_master
/// (supplier), which is not mapped yet.
/// </summary>
public class Vendor : AuditableEntity
{
    public int VendorId { get; set; }
    public string VendorCode { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? ContactPerson { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}
