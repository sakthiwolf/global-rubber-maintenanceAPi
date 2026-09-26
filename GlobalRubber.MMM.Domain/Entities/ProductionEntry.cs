using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.production_entry_transaction. References Machine, Product and Mold (all required, FK NO ACTION);
/// created_by/updated_by stay plain ids like every mapped table. GoodQty is a persisted computed column
/// (production_qty - rejected_qty) - read-only here. MoldUsageBefore/After are the mold's usage snapshot taken inside the
/// saving transaction (analysis BR-11). The analysis defines no edit, cancel or delete (6.1, Q-14): an entry is written
/// once with Status = Saved.
/// </summary>
public class ProductionEntry : AuditableEntity
{
    public int ProductionEntryId { get; set; }
    public string EntryNo { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string Shift { get; set; } = string.Empty;
    public int MachineId { get; set; }
    public int ProductId { get; set; }
    public int MoldId { get; set; }
    public int ProductionQty { get; set; }
    public int RejectedQty { get; set; }

    /// <summary>Computed by SQL Server (persisted): production_qty - rejected_qty.</summary>
    public int? GoodQty { get; set; }

    public int MoldUsageBefore { get; set; }
    public int MoldUsageAfter { get; set; }
    public string? Remarks { get; set; }
    public string Status { get; set; } = ProductionEntryStatus.Saved;

    public Machine Machine { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public Mold Mold { get; set; } = null!;
}
