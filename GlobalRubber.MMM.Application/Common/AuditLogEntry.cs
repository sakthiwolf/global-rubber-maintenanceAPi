namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Input to IAuditLogService.LogAsync - field names match audit.audit_log columns exactly
/// (minus event_at, which the service stamps itself via IDateTimeProvider). No invented fields.
/// </summary>
public sealed class AuditLogEntry
{
    public int? UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? EntityName { get; init; }
    public int? EntityId { get; init; }
    public string? RecordRef { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? IpAddress { get; init; }

    /// <summary>
    /// Optional field-level old/new values, written to audit.audit_log_detail in the same save as
    /// the audit_log row. Empty for events that have no field-level story (login, role created...).
    /// </summary>
    public IReadOnlyList<AuditLogDetailEntry> Details { get; init; } = Array.Empty<AuditLogDetailEntry>();
}

/// <summary>One audit_log_detail row - field names match its columns exactly (field_name/old_value/new_value).</summary>
public sealed record AuditLogDetailEntry(string FieldName, string? OldValue, string? NewValue);
