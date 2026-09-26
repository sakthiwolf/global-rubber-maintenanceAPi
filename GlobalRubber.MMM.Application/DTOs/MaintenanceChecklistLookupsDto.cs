namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Dropdown data for the Maintenance Checklist form (GET /api/v1/maintenance-checklists/lookups): the ACTIVE machines a
/// Machine checklist can be assigned to - one call, served under the checklist's own View permission (approved C6), so the
/// page never has to page through the Machine master.
/// </summary>
public sealed class MaintenanceChecklistLookupsDto
{
    public IReadOnlyList<MaintenanceChecklistMachineLookupDto> Machines { get; init; } = Array.Empty<MaintenanceChecklistMachineLookupDto>();
}

/// <summary>An active machine.</summary>
public sealed class MaintenanceChecklistMachineLookupDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
}
