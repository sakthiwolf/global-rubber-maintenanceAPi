namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Request body for PUT /api/v1/breakdown-types/{id}: update an existing breakdown type.</summary>
public sealed class UpdateBreakdownTypeRequest
{
    /// <summary>Name of the breakdown type; required, max 100 characters. The code cannot change. Duplicate active names are rejected.</summary>
    public string BreakdownTypeName { get; set; } = string.Empty;

    /// <summary>Base64-encoded row_version from the last read: the client sends it back, and the update fails with 409 if another user has modified the row.</summary>
    public string RowVersion { get; set; } = string.Empty;
}