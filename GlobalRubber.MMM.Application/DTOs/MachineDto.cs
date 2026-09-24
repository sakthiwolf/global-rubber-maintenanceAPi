namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Machine information returned to API callers (masters.machine_master joined to its department). The joined name lets
/// pages show it without needing Department view permission.
/// </summary>
public sealed class MachineDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string MachineType { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>Whether the machine's department is still active - lets a form keep an inactive one visible, flagged.</summary>
    public bool DepartmentIsActive { get; init; }

    public string Location { get; init; } = string.Empty;
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? Capacity { get; init; }
    public DateOnly? InstallationDate { get; init; }
    public int MaintenanceFrequencyDays { get; init; }

    // Responsible Engineer temporarily disabled. Database field and relationship intentionally retained for future re-enablement.
    // (Re-enable by restoring ResponsibleEngineerId / ResponsibleEngineerName / ResponsibleEngineerIsActive.)

    public string Criticality { get; init; } = string.Empty;

    /// <summary>System-managed (Running on create, then changed only by transactions) - read-only here.</summary>
    public string OperationalStatus { get; init; } = string.Empty;

    public DateOnly? LastMaintenanceDate { get; init; }
    public DateOnly? NextMaintenanceDate { get; init; }
    public string? Remarks { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the machine changed in between, the update
    /// is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
