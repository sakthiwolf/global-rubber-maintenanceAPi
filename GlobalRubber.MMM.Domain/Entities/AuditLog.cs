namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to audit.audit_log - "who changed what?" (business/security events). Append-only: no
/// update/delete path exists anywhere in the application. UserId is nullable (a failed login
/// has no resolved user) and UserName is a snapshot column by design, independent of UserId, so
/// no User navigation property is added here - it would suggest a live relationship this table
/// deliberately does not depend on.
/// </summary>
public class AuditLog
{
    public long AuditLogId { get; set; }
    public DateTime EventAt { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? RecordRef { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }

    public ICollection<AuditLogDetail> Details { get; set; } = new List<AuditLogDetail>();
}
