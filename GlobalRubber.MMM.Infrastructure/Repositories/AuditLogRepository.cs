using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public AuditLogRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
