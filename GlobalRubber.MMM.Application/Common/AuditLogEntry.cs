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
}
