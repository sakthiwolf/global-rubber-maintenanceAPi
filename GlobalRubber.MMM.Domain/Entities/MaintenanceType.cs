using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.maintenance_type_master. The table has no foreign key to another master (only created_by/updated_by
/// to security.user_master, kept as plain ids like every mapped table); it is referenced by
/// transactions.machine_pm_transaction, which is not mapped yet.
/// </summary>
public class MaintenanceType : AuditableEntity
{
    public int MaintenanceTypeId { get; set; }
    public string MaintenanceTypeCode { get; set; } = string.Empty;
    public string MaintenanceTypeName { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
