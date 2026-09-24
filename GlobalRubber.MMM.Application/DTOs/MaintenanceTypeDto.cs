namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Maintenance type information returned to API callers (masters.maintenance_type_master).</summary>
public sealed class MaintenanceTypeDto
{
    public int MaintenanceTypeId { get; init; }
    public string MaintenanceTypeCode { get; init; } = string.Empty;
    public string MaintenanceTypeName { get; init; } = string.Empty;

    /// <summary>Machine / Mold / Both.</summary>
    public string AppliesTo { get; init; } = string.Empty;

    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the maintenance type changed in between,
    /// the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
