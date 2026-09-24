using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Failure-behavior decision (explicit, not silent): audit-logging failure never fails the
/// business operation it describes, and never surfaces to the caller. A login (or any other
/// audited action) must still succeed for the user even if the audit INSERT itself fails (e.g.
/// a transient DB issue) - turning a logging concern into an availability outage would be worse
/// than a missed audit row. The failure is still captured via ILogger, so it is not silently
/// swallowed from an operations standpoint, only from the caller's.
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository auditLogRepository, IDateTimeProvider dateTimeProvider, ILogger<AuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = new AuditLog
            {
                EventAt = _dateTimeProvider.UtcNow,
                UserId = entry.UserId,
                UserName = entry.UserName,
                Module = entry.Module,
                Action = entry.Action,
                EntityName = entry.EntityName,
                EntityId = entry.EntityId,
                RecordRef = entry.RecordRef,
                Description = entry.Description,
                IpAddress = entry.IpAddress,
            };

            await _auditLogRepository.AddAsync(auditLog, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry for {Module}.{Action}.", entry.Module, entry.Action);
        }
    }
}
