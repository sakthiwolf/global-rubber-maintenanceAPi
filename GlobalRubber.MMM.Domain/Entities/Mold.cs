using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.mold_master. References Product (required) and Employee as the responsible person (optional);
/// CreatedBy/UpdatedBy stay plain nullable ids like every mapped table. There is no IsActive: the mold lifecycle is
/// managed through Status, with 'Retired' in the role IsActive plays elsewhere. LifeState is a persisted computed
/// column - read-only here. Referenced by production entries, mold PMs, work orders and spare-part usage (not mapped yet).
/// </summary>
public class Mold : AuditableEntity
{
    public int MoldId { get; set; }
    public string MoldCode { get; set; } = string.Empty;
    public string MoldName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string MoldType { get; set; } = string.Empty;
    public int CavityCount { get; set; } = 1;
    public string? Manufacturer { get; set; }
    public string? SerialNumber { get; set; }
    public string? Location { get; set; }
    public string? StorageLocation { get; set; }
    public DateOnly? CommissionDate { get; set; }
    public int MaximumShots { get; set; } = 500000;
    public int WarningShots { get; set; } = 450000;
    public int ReplacementShots { get; set; } = 500000;
    public int? MaintenanceFrequencyShots { get; set; }
    public int CurrentUsageShots { get; set; }
    public int? ResponsibleEmployeeId { get; set; }
    public string Status { get; set; } = MoldStatus.Available;

    /// <summary>Computed by SQL Server (persisted): Replace / Warning / Normal from usage vs. the thresholds.</summary>
    public string LifeState { get; set; } = MoldLifeState.Normal;

    public string? Remarks { get; set; }

    public Product Product { get; set; } = null!;
    public Employee? ResponsibleEmployee { get; set; }
}
