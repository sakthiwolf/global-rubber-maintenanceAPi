namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/spare-parts/{id} - a full replacement of the editable fields plus the row version the
/// caller last read (409 if the spare part changed since - e.g. a usage transaction reduced its stock). Not accepted: the
/// id, the code (immutable), stockStatus (computed), isActive (deactivation is DELETE), audit columns.
/// </summary>
public sealed class UpdateSparePartRequest : CreateSparePartRequest
{
    /// <summary>Base64 row_version exactly as returned by SparePartDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
