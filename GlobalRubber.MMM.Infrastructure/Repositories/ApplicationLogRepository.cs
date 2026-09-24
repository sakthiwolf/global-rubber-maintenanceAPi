using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class ApplicationLogRepository : IApplicationLogRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public ApplicationLogRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ApplicationLog applicationLog, CancellationToken cancellationToken)
    {
        _dbContext.ApplicationLogs.Add(applicationLog);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
