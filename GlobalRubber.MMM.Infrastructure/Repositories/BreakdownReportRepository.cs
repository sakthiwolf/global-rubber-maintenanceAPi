using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>Read-only Breakdown Analysis queries: filter (MachineReportQueryBuilder) -> GROUP BY -> sort -> page, all in SQL.</summary>
public sealed class BreakdownReportRepository : IBreakdownReportRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public BreakdownReportRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<(IReadOnlyList<BreakdownGroupRow> Items, int TotalCount)> GetGroupsAsync(
        BreakdownAnalysisReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_dbContext.MachineBreakdowns.AsNoTracking(), query);
        var groups = BreakdownReportQueryBuilder.Group(BreakdownReportQueryBuilder.ToGroupSource(filtered, query.GroupBy ?? BreakdownReportGroupBy.Machine));
        var ordered = BreakdownReportQueryBuilder.OrderGroups(groups, query);

        return ReportPaging.PageAsync(groups, ordered, page, cancellationToken);
    }

    public async Task<BreakdownReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        var machines = await _dbContext.Machines.AsNoTracking()
            .OrderBy(m => m.MachineCode)
            .Select(m => new ReportLookupItemDto { Id = m.MachineId, Code = m.MachineCode, Name = m.MachineName, IsActive = m.IsActive })
            .ToListAsync(cancellationToken);

        var departments = await _dbContext.Departments.AsNoTracking()
            .Where(d => _dbContext.Machines.Any(m => m.DepartmentId == d.DepartmentId))
            .OrderBy(d => d.DepartmentName)
            .Select(d => new ReportLookupItemDto { Id = d.DepartmentId, Code = d.DepartmentCode, Name = d.DepartmentName, IsActive = d.IsActive })
            .ToListAsync(cancellationToken);

        var types = await _dbContext.BreakdownTypes.AsNoTracking()
            .OrderBy(t => t.BreakdownTypeName)
            .Select(t => new ReportLookupItemDto { Id = t.BreakdownTypeId, Code = t.BreakdownTypeCode, Name = t.BreakdownTypeName, IsActive = t.IsActive })
            .ToListAsync(cancellationToken);

        return new BreakdownReportLookupsDto
        {
            Machines = machines,
            Departments = departments,
            BreakdownTypes = types,
            Priorities = BreakdownPriority.All,
            Stages = BreakdownStage.All,
            Statuses = MachineReportBreakdownStatus.All,
            HistoryStages = new[] { BreakdownStage.Resolved, BreakdownStage.Closed },
            GroupBys = BreakdownReportGroupBy.All,
        };
    }
}
