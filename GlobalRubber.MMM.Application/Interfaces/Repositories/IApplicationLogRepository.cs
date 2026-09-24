using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IApplicationLogRepository
{
    Task AddAsync(ApplicationLog applicationLog, CancellationToken cancellationToken);
}
