using GlobalRubber.MMM.Application.Interfaces.Repositories;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// audit_log.user_name is a NOT NULL snapshot column and the JWT carries no user name, so services look
/// it up from the acting user id. An unresolvable or failing lookup never blocks the (already completed)
/// business write: it degrades to "Unknown" and is logged, consistent with AuditLogService's own
/// failure decision.
/// </summary>
internal static class AuditUserNameResolver
{
    public static async Task<string> ResolveAsync(
        IUserRepository userRepository, ILogger logger, int? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return "Unknown";
        }

        try
        {
            var user = await userRepository.GetByIdAsync(userId.Value, cancellationToken);

            return user?.UserName ?? "Unknown";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not resolve the user name for audit user id {UserId}.", userId);

            return "Unknown";
        }
    }
}
