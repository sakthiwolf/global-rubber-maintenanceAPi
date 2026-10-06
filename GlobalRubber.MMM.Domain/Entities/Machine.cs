using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.machine_master. References Department (required) and Employee as the responsible engineer
/// (optional); CreatedBy/UpdatedBy stay plain nullable ids like every mapped table. OperationalStatus and the
/// last/next maintenance dates are system-managed (set by transactions), never by the Machine form.
/// Referenced by spare parts, production entries, breakdowns, PMs, work orders and spare-part usage - none mapped yet.
/// </summary>
public class Machine : AuditableEntity
{
    public int MachineId { get; set; }
    public string MachineCode { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string MachineType { get; set; } = string.Empty;
    /// <summary>Optional since migration 023 (F01 has no department; it can be assigned later).</summary>
    public int? DepartmentId { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Capacity { get; set; }
    public DateOnly? InstallationDate { get; set; }

    /// <summary>Migration 022 - F01 "Range" (e.g. "40/40 Inches"); free text, optional.</summary>
    public string? MachineRange { get; set; }

    /// <summary>Migration 022 - F01 "Ownership" (e.g. "GRP", "DAI"); free text, optional.</summary>
    public string? Ownership { get; set; }

    /// <summary>Migration 022 - F01 "Month &amp; Year of Purchase"; optional. Not the installation date.</summary>
    public DateOnly? PurchaseDate { get; set; }
    public int MaintenanceFrequencyDays { get; set; } = 30;
    public int? ResponsibleEngineerId { get; set; }
    public string Criticality { get; set; } = MachineCriticality.Medium;
    public string OperationalStatus { get; set; } = MachineOperationalStatus.Running;
    public DateOnly? LastMaintenanceDate { get; set; }
    public DateOnly? NextMaintenanceDate { get; set; }
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;

    public Department? Department { get; set; } // null when the machine has no department (migration 023)
    public Employee? ResponsibleEngineer { get; set; }
}
