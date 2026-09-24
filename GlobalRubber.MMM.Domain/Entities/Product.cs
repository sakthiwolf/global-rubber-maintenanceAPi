using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.product_master. The table has no foreign key to another master (only created_by/updated_by to
/// security.user_master, kept as plain ids like every mapped table); it is referenced by masters.mold_master and
/// transactions.production_entry_transaction, neither mapped yet.
/// </summary>
public class Product : AuditableEntity
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;
    public decimal? StandardCycleTimeSec { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}
