namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to audit.application_log - "what happened technically?" (diagnostics), kept strictly
/// separate from AuditLog. Append-only. No User navigation for the same reason as AuditLog.
/// </summary>
public class ApplicationLog
{
    public long ApplicationLogId { get; set; }
    public string LogLevel { get; set; } = string.Empty;
    public DateTime LogDateTime { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string? Source { get; set; }
    public string? RequestPath { get; set; }
    public string? HttpMethod { get; set; }
    public int? UserId { get; set; }
    public string? CorrelationId { get; set; }
    public string? MachineName { get; set; }
    public DateTime CreatedAt { get; set; }
}
