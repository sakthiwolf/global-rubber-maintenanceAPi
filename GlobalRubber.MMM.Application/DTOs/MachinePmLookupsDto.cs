namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Dropdown data for the Machine PM page (GET /api/v1/machine-maintenance/lookups): the ACTIVE machines, for the machine
/// filter. Served under the Machine PM permission so the page never depends on master-page permissions.
/// </summary>
public sealed class MachinePmLookupsDto
{
    public IReadOnlyList<MachinePmMachineLookupDto> Machines { get; init; } = Array.Empty<MachinePmMachineLookupDto>();
}

public sealed class MachinePmMachineLookupDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}
