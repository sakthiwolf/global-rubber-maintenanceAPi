namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/molds/{id} - a full replacement of the editable fields plus the row version the caller
/// last read (409 if the mold changed since). Not accepted: moldId, moldCode (immutable), lifeState (computed), audit
/// columns.
/// </summary>
public sealed class UpdateMoldRequest : CreateMoldRequest
{
    /// <summary>
    /// Manual usage correction (template "edit mode only" field, BR-14: "Adjust only for corrections - normally updated
    /// automatically by production entries"). Omitted/null keeps the stored value. Must be &gt;= 0
    /// (CK_mold_master_current_usage_shots). A change is always audited with its old and new value.
    /// </summary>
    public int? CurrentUsageShots { get; init; }

    /// <summary>Base64 row_version exactly as returned by MoldDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
