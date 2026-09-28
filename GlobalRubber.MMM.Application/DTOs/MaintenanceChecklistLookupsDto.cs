namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Dropdown data for the Maintenance Checklist form (GET /api/v1/maintenance-checklists/lookups): the ACTIVE machines a
/// Machine checklist can be assigned to - one call, served under the checklist's own View permission (approved C6), so the
/// page never has to page through the Machine master - plus the ACTIVE maintenance types a Machine checklist can carry
/// (applies to Machine or Both; migration 017).
/// </summary>
public sealed class MaintenanceChecklistLookupsDto
{
    public IReadOnlyList<MaintenanceChecklistMachineLookupDto> Machines { get; init; } = Array.Empty<MaintenanceChecklistMachineLookupDto>();
    public IReadOnlyList<MaintenanceChecklistTypeLookupDto> MaintenanceTypes { get; init; } = Array.Empty<MaintenanceChecklistTypeLookupDto>();
}

/// <summary>An active maintenance type that applies to Machine or Both.</summary>
public sealed class MaintenanceChecklistTypeLookupDto
{
    public int MaintenanceTypeId { get; init; }
    public string MaintenanceTypeCode { get; init; } = string.Empty;
    public string MaintenanceTypeName { get; init; } = string.Empty;
}

/// <summary>An active machine.</summary>
public sealed class MaintenanceChecklistMachineLookupDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
}
