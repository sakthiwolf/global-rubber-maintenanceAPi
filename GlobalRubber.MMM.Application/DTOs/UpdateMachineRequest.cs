namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/machines/{id} - a full replacement of the editable fields plus the row version the
/// caller last read (409 if the machine changed since). Not accepted: machineId, machineCode (immutable), isActive
/// (deactivation is DELETE), operationalStatus and last/next maintenance dates (system-managed), audit columns.
/// </summary>
public sealed class UpdateMachineRequest : CreateMachineRequest
{
    /// <summary>Base64 row_version exactly as returned by MachineDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
