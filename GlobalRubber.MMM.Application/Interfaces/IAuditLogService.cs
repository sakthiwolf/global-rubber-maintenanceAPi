using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IAuditLogService
{
    /// <summary>
    /// Records a business/security audit event to audit.audit_log. Never throws - see
    /// AuditLogService's remarks for why a logging failure must not fail the business
    /// operation it is describing.
    /// </summary>
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}
