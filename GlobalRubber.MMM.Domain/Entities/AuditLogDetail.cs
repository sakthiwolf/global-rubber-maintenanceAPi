namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to audit.audit_log_detail - field-level old/new values for one AuditLog row. Not
/// written yet by any service this phase (see the audit step's report on permission-change
/// auditing) - mapped now so the table is ready when that work happens.
/// </summary>
public class AuditLogDetail
{
    public long AuditLogDetailId { get; set; }
    public long AuditLogId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public AuditLog AuditLog { get; set; } = null!;
}
