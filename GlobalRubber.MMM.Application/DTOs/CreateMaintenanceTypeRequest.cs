namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/maintenance-types (analysis 4.8: Name*, Applies To*). Not accepted (and ignored by
/// model binding if sent): maintenanceTypeId, maintenanceTypeCode (issued from the MAINTENANCE_TYPE document sequence),
/// isActive (a new maintenance type is always active), rowVersion and the audit columns.
/// </summary>
public class CreateMaintenanceTypeRequest
{
    public string MaintenanceTypeName { get; init; } = string.Empty;

    /// <summary>Required: Machine / Mold / Both (CK_maintenance_type_master_applies_to).</summary>
    public string? AppliesTo { get; init; }
}
