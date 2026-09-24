namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/machines - the fields on the Add Machine form (analysis 4.1). Not accepted (and
/// ignored by model binding if sent): machineId, machineCode (issued from the MACHINE document sequence), isActive
/// (a new machine is always active), operationalStatus (system-managed: Running on create), last/next maintenance
/// dates (set by PM/breakdown transactions), rowVersion and the audit columns.
/// </summary>
public class CreateMachineRequest
{
    public string MachineName { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public string Location { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? Capacity { get; init; }
    public DateOnly? InstallationDate { get; init; }

    /// <summary>Required, &gt; 0 (CK_machine_master_maintenance_frequency_days). The form defaults it to 30.</summary>
    public int? MaintenanceFrequencyDays { get; init; }

    // Responsible Engineer temporarily disabled. Database field and relationship intentionally retained for future re-enablement.
    // (Re-enable by restoring: public int? ResponsibleEngineerId { get; init; })

    /// <summary>Required: Low / Medium / High (CK_machine_master_criticality). The form defaults it to Medium.</summary>
    public string? Criticality { get; init; }

    public string? Remarks { get; init; }
}
