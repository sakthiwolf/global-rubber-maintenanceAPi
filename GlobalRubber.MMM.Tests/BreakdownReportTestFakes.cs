using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// IBreakdownReportRepository over MachineReportScenario's breakdowns (the same data InMemoryMachineReportRepository
/// serves), running the SAME MachineReportQueryBuilder filters and BreakdownReportQueryBuilder grouping the EF repository
/// translates to SQL. Honours cancellation like EF does.
///
/// Scenario (see MachineReportScenario): BRK-0001 MAC-0001 (DEP-0001) High Closed Electrical 2.5 h "Op A";
/// BRK-0002 MAC-0002 (DEP-0003) Critical Resolved Mechanical 6.0 h; BRK-0003 MAC-0001 Low Maintenance Started Electrical (open);
/// BRK-0004 MAC-0002 Medium Reported no type (open); BRK-0005 MAC-0003 (DEP-0002) Critical Closed no type 1.0 h.
/// </summary>
internal sealed class InMemoryBreakdownReportRepository : IBreakdownReportRepository
{
    private readonly MachineReportScenario _s;

    public InMemoryBreakdownReportRepository(MachineReportScenario scenario)
    {
        _s = scenario;
    }

    public List<(string Method, ReportPage? Page)> Calls { get; } = new();

    public Task<(IReadOnlyList<BreakdownGroupRow> Items, int TotalCount)> GetGroupsAsync(
        BreakdownAnalysisReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetGroupsAsync), page));
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_s.Breakdowns.AsQueryable(), query);
        var groups = BreakdownReportQueryBuilder.Group(BreakdownReportQueryBuilder.ToGroupSource(filtered, query.GroupBy ?? BreakdownReportGroupBy.Machine));
        var ordered = BreakdownReportQueryBuilder.OrderGroups(groups, query).ToList();
        IReadOnlyList<BreakdownGroupRow> items = page is null ? ordered : ordered.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, ordered.Count));
    }

    public Task<BreakdownReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetLookupsAsync), null));
        static ReportLookupItemDto Item(int id, string code, string name, bool active) => new() { Id = id, Code = code, Name = name, IsActive = active };

        return Task.FromResult(new BreakdownReportLookupsDto
        {
            Machines = _s.Machines.Select(m => Item(m.MachineId, m.MachineCode, m.MachineName, m.IsActive)).ToList(),
            Departments = _s.Departments.Where(d => _s.Machines.Any(m => m.DepartmentId == d.DepartmentId)).Select(d => Item(d.DepartmentId, d.DepartmentCode, d.DepartmentName, d.IsActive)).ToList(),
            BreakdownTypes = _s.BreakdownTypes.Select(t => Item(t.BreakdownTypeId, t.BreakdownTypeCode, t.BreakdownTypeName, t.IsActive)).ToList(),
            Priorities = BreakdownPriority.All,
            Stages = BreakdownStage.All,
            Statuses = MachineReportBreakdownStatus.All,
            HistoryStages = new[] { BreakdownStage.Resolved, BreakdownStage.Closed },
            GroupBys = BreakdownReportGroupBy.All,
        });
    }
}
