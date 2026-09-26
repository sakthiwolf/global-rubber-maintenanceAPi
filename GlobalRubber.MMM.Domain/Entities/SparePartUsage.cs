using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.spare_part_usage_transaction: one spare part issued against one maintenance record (migration
/// 016). Posted usage is never edited or deleted - a reversal (Status Reversed) restores the stock. The maintenance is
/// referenced by id (MachinePmId or MoldPmId, per UsedFor - CK_spare_part_usage_transaction_maintenance_source); MachineId /
/// MoldId are copied from that PM so the usage keeps its asset even if the asset is renamed. UnitCostAtIssue snapshots the
/// master's unit cost. RequestId is the client's idempotency key (unique when present).
/// </summary>
public class SparePartUsage : AuditableEntity
{
    public int SparePartUsageId { get; set; }
    public string UsageNo { get; set; } = string.Empty;
    public int SparePartId { get; set; }
    public int Quantity { get; set; }
    public DateOnly UsageDate { get; set; }
    public string UsedFor { get; set; } = string.Empty;
    public string? ReferenceNo { get; set; }
    public int? MachineId { get; set; }
    public int? MoldId { get; set; }
    public int? MachinePmId { get; set; }
    public int? MoldPmId { get; set; }
    public int? UsedByEmployeeId { get; set; }
    public decimal? UnitCostAtIssue { get; set; }
    public string? Remarks { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? ReversedAt { get; set; }
    public int? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public Guid? RequestId { get; set; }

    public SparePart SparePart { get; set; } = null!;
    public Machine? Machine { get; set; }
    public Mold? Mold { get; set; }
    public MachinePm? MachinePm { get; set; }
    public MoldPm? MoldPm { get; set; }
    public Employee? UsedByEmployee { get; set; }
}
