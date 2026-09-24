namespace GlobalRubber.MMM.Domain.Common;

/// <summary>
/// Shared audit columns (created_at/by, updated_at/by, row_version) present on every
/// security table. CreatedBy/UpdatedBy are nullable ints, not navigation properties -
/// the database allows them to be null for bootstrap rows created before any user exists.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
