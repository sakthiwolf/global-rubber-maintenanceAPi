using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// A fixed report scenario (plant "today" = 2026-09-26). Machines/departments are MachineTestData/DepartmentTestData
/// with their navigations wired, plus PMs and breakdowns whose expected figures are spelled out in the tests.
///
///   PMs:  MPM-0001 MAC-0001 CHK-0001 Daily, due 09-20, done 09-20 by Ravi (1 of 2 items checked)
///         MPM-0002 MAC-0001 CHK-0001 Daily, due 09-21, done 09-22 by Kumar
///         MPM-0003 MAC-0002 no plan, type "Preventive", due 09-10, done 09-11 by Ravi
///         MPM-0004 MAC-0001 CHK-0001 Daily, due 09-25, open -> Overdue
///         MPM-0005 MAC-0002 CHK-0002 Weekly, due 09-30, open -> Scheduled
///   Breakdowns (downtime = stored downtime_hours):
///         BRK-0001 MAC-0001 09-01 08:00 High     Closed              Electrical  Suresh  2.5 h
///         BRK-0002 MAC-0002 09-05 10:00 Critical Resolved            Mechanical          6.0 h
///         BRK-0003 MAC-0001 09-10 09:00 Low      Maintenance Started Electrical  Suresh  (open)
///         BRK-0004 MAC-0002 09-12 14:00 Medium   Reported                                (open)
///         BRK-0005 MAC-0003 09-15 11:00 Critical Closed                                  1.0 h  "Hydraulic, leak"
///   => 5 breakdowns, 2 open, 3 resolved, total 9.5 h, average 3.17 h, longest 6.0 h (BRK-0002).
/// </summary>
internal sealed class MachineReportScenario
{
    public static readonly DateOnly Today = new(2026, 9, 26);

    public List<Department> Departments { get; } = DepartmentTestData.Departments();
    public List<Machine> Machines { get; } = MachineTestData.Machines();
    public List<MachinePm> Pms { get; } = new();
    public List<MachineBreakdown> Breakdowns { get; } = new();
    public List<BreakdownType> BreakdownTypes { get; } = new();
    public List<MaintenanceType> MaintenanceTypes { get; } = new();
    public List<MaintenanceChecklist> Checklists { get; } = new();

    public MachineReportScenario()
    {
        foreach (var m in Machines)
            m.Department = Departments.Single(d => d.DepartmentId == m.DepartmentId);

        Machine M(int id) => Machines.Single(m => m.MachineId == id);

        var daily = new MaintenanceChecklist { ChecklistId = 11, ChecklistCode = "CHK-0001", ChecklistName = "Daily oil", AppliesTo = "Machine", Frequency = "Daily", MachineId = 1, IsActive = true };
        var weekly = new MaintenanceChecklist { ChecklistId = 12, ChecklistCode = "CHK-0002", ChecklistName = "Weekly check", AppliesTo = "Machine", Frequency = "Weekly", MachineId = 2, IsActive = true };
        var moldPlan = new MaintenanceChecklist { ChecklistId = 13, ChecklistCode = "CHK-0003", ChecklistName = "Mold plan", AppliesTo = "Mold", IsActive = true };
        Checklists.AddRange(new[] { daily, weekly, moldPlan });

        var preventive = new MaintenanceType { MaintenanceTypeId = 21, MaintenanceTypeCode = "MT-0001", MaintenanceTypeName = "Preventive", AppliesTo = "Machine", IsActive = true };
        var moldOnly = new MaintenanceType { MaintenanceTypeId = 22, MaintenanceTypeCode = "MT-0002", MaintenanceTypeName = "Mold polish", AppliesTo = "Mold", IsActive = true };
        var both = new MaintenanceType { MaintenanceTypeId = 23, MaintenanceTypeCode = "MT-0003", MaintenanceTypeName = "Calibration", AppliesTo = "Both", IsActive = false };
        MaintenanceTypes.AddRange(new[] { preventive, moldOnly, both });

        Pms.Add(Pm(1, "MPM-0001", M(1), daily, new(2026, 9, 20), new(2026, 9, 20), "Ravi", items: new[] { true, false }));
        Pms.Add(Pm(2, "MPM-0002", M(1), daily, new(2026, 9, 21), new(2026, 9, 22), "Kumar"));
        Pms.Add(Pm(3, "MPM-0003", M(2), null, new(2026, 9, 10), new(2026, 9, 11), "Ravi", type: preventive));
        Pms.Add(Pm(4, "MPM-0004", M(1), daily, new(2026, 9, 25), null, null));
        Pms.Add(Pm(5, "MPM-0005", M(2), weekly, new(2026, 9, 30), null, null));

        var electrical = new BreakdownType { BreakdownTypeId = 31, BreakdownTypeCode = "BT-0001", BreakdownTypeName = "Electrical", IsActive = true };
        var mechanical = new BreakdownType { BreakdownTypeId = 32, BreakdownTypeCode = "BT-0002", BreakdownTypeName = "Mechanical", IsActive = false };
        BreakdownTypes.AddRange(new[] { electrical, mechanical });
        var suresh = new Employee { EmployeeId = 41, EmployeeCode = "EMP-0041", EmployeeName = "Suresh" };

        Breakdowns.Add(Bd(1, "BRK-0001", M(1), new(2026, 9, 1), new(8, 0), BreakdownPriority.High, BreakdownStage.Closed, electrical, suresh,
            started: Utc(2026, 9, 1, 3, 0), resolved: Utc(2026, 9, 1, 5, 30), downtime: 2.5m, reportedBy: "Op A", problem: "Heater failed"));
        Breakdowns.Add(Bd(2, "BRK-0002", M(2), new(2026, 9, 5), new(10, 0), BreakdownPriority.Critical, BreakdownStage.Resolved, mechanical, null,
            started: Utc(2026, 9, 5, 5, 0), resolved: Utc(2026, 9, 5, 11, 0), downtime: 6.0m, problem: "Gearbox"));
        Breakdowns.Add(Bd(3, "BRK-0003", M(1), new(2026, 9, 10), new(9, 0), BreakdownPriority.Low, BreakdownStage.MaintenanceStarted, electrical, suresh,
            started: Utc(2026, 9, 10, 4, 0), problem: "Loose wire"));
        Breakdowns.Add(Bd(4, "BRK-0004", M(2), new(2026, 9, 12), new(14, 0), BreakdownPriority.Medium, BreakdownStage.Reported, null, null, problem: "Noise"));
        Breakdowns.Add(Bd(5, "BRK-0005", M(3), new(2026, 9, 15), new(11, 0), BreakdownPriority.Critical, BreakdownStage.Closed, null, null,
            started: Utc(2026, 9, 15, 6, 0), resolved: Utc(2026, 9, 15, 7, 0), downtime: 1.0m, problem: "Hydraulic, leak"));
    }

    private static DateTime Utc(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0);

    private static MachinePm Pm(int id, string no, Machine machine, MaintenanceChecklist? checklist, DateOnly due, DateOnly? done,
        string? by, MaintenanceType? type = null, bool[]? items = null) => new()
    {
        MachinePmId = id, PmNo = no, MachineId = machine.MachineId, Machine = machine,
        ChecklistId = checklist?.ChecklistId, Checklist = checklist,
        MaintenanceTypeId = type?.MaintenanceTypeId, MaintenanceType = type,
        ScheduledDate = due, CompletedDate = done, MaintenanceBy = by,
        Status = done is null ? MachinePmStatus.Scheduled : MachinePmStatus.Completed,
        ChecklistItems = (items ?? Array.Empty<bool>())
            .Select((c, i) => new MachinePmChecklistItem { MachinePmChecklistId = id * 10 + i, MachinePmId = id, SortOrder = i + 1, ItemLabel = $"Item {i + 1}", IsChecked = c })
            .ToList(),
    };

    private static MachineBreakdown Bd(int id, string no, Machine machine, DateOnly date, TimeOnly time, string priority, string stage,
        BreakdownType? type, Employee? engineer, DateTime? started = null, DateTime? resolved = null, decimal? downtime = null,
        string? reportedBy = null, string problem = "Problem") => new()
    {
        MachineBreakdownId = id, BreakdownNo = no, MachineId = machine.MachineId, Machine = machine,
        BreakdownDate = date, BreakdownTime = time, Priority = priority, Stage = stage,
        BreakdownTypeId = type?.BreakdownTypeId, BreakdownType = type,
        AssignedEngineerId = engineer?.EmployeeId, AssignedEngineer = engineer,
        MaintenanceStartedAt = started, ResolvedAt = resolved, ClosedAt = stage == BreakdownStage.Closed ? resolved?.AddHours(1) : null,
        DowntimeHours = downtime, ReportedBy = reportedBy, Problem = problem,
    };
}

/// <summary>
/// IMachineReportRepository over the scenario's lists, running the SAME MachineReportQueryBuilder the EF repository
/// translates to SQL (LINQ-to-objects here). Records whether each call was paged, so tests can prove exports ask for
/// the whole filtered set.
/// </summary>
internal sealed class InMemoryMachineReportRepository : IMachineReportRepository
{
    private readonly MachineReportScenario _s;

    public InMemoryMachineReportRepository(MachineReportScenario scenario)
    {
        _s = scenario;
    }

    public List<(string Method, ReportPage? Page)> Calls { get; } = new();

    public Task<(IReadOnlyList<MachineListReportItemDto> Items, int TotalCount)> GetMachineListAsync(
        MachineListReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetMachineListAsync), page));
        var filtered = MachineReportQueryBuilder.FilterMachines(_s.Machines.AsQueryable(), query);
        return Task.FromResult(Page(filtered, MachineReportQueryBuilder.OrderMachines(filtered).Select(MachineReportQueryBuilder.MachineRow), page));
    }

    public Task<(IReadOnlyList<MachineMaintenanceHistoryReportItemDto> Items, int TotalCount)> GetMaintenanceHistoryAsync(
        MachineMaintenanceHistoryReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetMaintenanceHistoryAsync), page));
        var filtered = MachineReportQueryBuilder.FilterMaintenanceHistory(_s.Pms.AsQueryable(), query, today);
        return Task.FromResult(Page(filtered,
            MachineReportQueryBuilder.OrderMaintenanceHistory(filtered).Select(MachineReportQueryBuilder.MaintenanceHistoryRow(today)), page));
    }

    public Task<(IReadOnlyList<MachineBreakdownReportItemDto> Items, int TotalCount)> GetBreakdownsAsync(
        MachineBreakdownReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetBreakdownsAsync), page));
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_s.Breakdowns.AsQueryable(), query);
        return Task.FromResult(Page(filtered,
            MachineReportQueryBuilder.OrderBreakdowns(filtered, query).Select(MachineReportQueryBuilder.BreakdownRow), page));
    }

    public Task<MachineDowntimeSummaryDto> GetDowntimeSummaryAsync(MachineBreakdownReportQuery query, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetDowntimeSummaryAsync), null));
        var filtered = MachineReportQueryBuilder.FilterBreakdowns(_s.Breakdowns.AsQueryable(), query);
        var aggregate = MachineReportQueryBuilder.DowntimeAggregate(filtered).FirstOrDefault();
        var longest = MachineReportQueryBuilder.LongestDowntimeBreakdownNo(filtered).FirstOrDefault();
        return Task.FromResult(MachineReportQueryBuilder.ToSummary(aggregate, longest));
    }

    public Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetLookupsAsync), null));
        static ReportLookupItemDto Item(int id, string code, string name, bool active) => new() { Id = id, Code = code, Name = name, IsActive = active };

        return Task.FromResult(new MachineReportLookupsDto
        {
            Machines = _s.Machines.OrderBy(m => m.MachineCode).Select(m => Item(m.MachineId, m.MachineCode, m.MachineName, m.IsActive)).ToList(),
            Departments = _s.Departments.Where(d => _s.Machines.Any(m => m.DepartmentId == d.DepartmentId))
                .OrderBy(d => d.DepartmentName).Select(d => Item(d.DepartmentId, d.DepartmentCode, d.DepartmentName, d.IsActive)).ToList(),
            MachineTypes = _s.Machines.Select(m => m.MachineType).Distinct().OrderBy(t => t).ToList(),
            BreakdownTypes = _s.BreakdownTypes.Select(t => Item(t.BreakdownTypeId, t.BreakdownTypeCode, t.BreakdownTypeName, t.IsActive)).ToList(),
            MaintenanceTypes = _s.MaintenanceTypes.Where(t => t.AppliesTo is MaintenanceTypeAppliesTo.Machine or MaintenanceTypeAppliesTo.Both)
                .Select(t => Item(t.MaintenanceTypeId, t.MaintenanceTypeCode, t.MaintenanceTypeName, t.IsActive)).ToList(),
            MaintenancePlans = _s.Checklists.Where(c => c.AppliesTo == MaintenanceChecklistAppliesTo.Machine)
                .Select(c => Item(c.ChecklistId, c.ChecklistCode, c.ChecklistName, c.IsActive)).ToList(),
            Criticalities = MachineCriticality.All,
            OperationalStatuses = MachineOperationalStatus.All,
            Priorities = BreakdownPriority.All,
            Stages = BreakdownStage.All,
            MaintenanceStatuses = MachineReportPmStatus.All,
            BreakdownStatuses = MachineReportBreakdownStatus.All,
        });
    }

    private static (IReadOnlyList<T> Items, int TotalCount) Page<TEntity, T>(IQueryable<TEntity> filtered, IQueryable<T> rows, ReportPage? page)
    {
        if (page is null)
        {
            var all = rows.ToList();
            return (all, all.Count);
        }

        return (rows.Skip(page.Skip).Take(page.PageSize).ToList(), filtered.Count());
    }
}
