namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Breakdown type information returned to API callers (masters.breakdown_type_master).</summary>
public sealed class BreakdownTypeDto
{
    public int BreakdownTypeId { get; init; }
    public string BreakdownTypeCode { get; init; } = string.Empty;
    public string BreakdownTypeName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }
    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the breakdown type changed in between,
    /// the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}