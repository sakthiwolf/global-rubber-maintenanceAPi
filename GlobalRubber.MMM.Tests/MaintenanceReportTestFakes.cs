using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// A fixed Maintenance Reports scenario (plant "today" = 2026-09-28).
///   Machine PMs: MPM-0001 MAC-0001 CHK-0001 due 09-20 done 09-20 by Ravi (1/2 checked)  -> Completed
///                MPM-0002 MAC-0001 CHK-0001 due 09-25 Scheduled                         -> Overdue (3 days)
///                MPM-0003 MAC-0002 type Preventive due 10-05 Scheduled                  -> Scheduled
///                MPM-0004 MAC-0002 due 09-28 In Progress                                -> In Progress
///   Mold PMs:    MPMD-0001 MLD-0001 Shot-based due 09-26 Scheduled, threshold 450,000   -> Overdue (2 days), remaining -10,000
///                MPMD-0002 MLD-0002 Shot-based due 09-28 Scheduled, threshold 80,000    -> Due (today), remaining -10,000 (usage 90,000)
///                MPMD-0003 MLD-0002 Cleaning due 09-10 done 09-12 by Kumar              -> Completed
///                MPMD-0004 MLD-0001 Inspection due 09-27 In Progress                    -> In Progress
/// </summary>
internal sealed class MaintenanceReportScenario
{
    public static readonly DateOnly Today = new(2026, 9, 28);

    public List<Machine> Machines { get; } = MachineTestData.Machines();
    public List<Mold> Molds { get; } = new()
    {
        new Mold { MoldId = 1, MoldCode = "MLD-0001", MoldName = "Cap Mold", CurrentUsageShots = 460_000, Status = MoldStatus.InProduction },
        new Mold { MoldId = 2, MoldCode = "MLD-0002", MoldName = "Bottle Mold", CurrentUsageShots = 90_000, Status = MoldStatus.Retired },
    };
    public List<MaintenanceChecklist> Checklists { get; } = new()
    {
        new MaintenanceChecklist { ChecklistId = 11, ChecklistCode = "CHK-0001", ChecklistName = "Daily oil", AppliesTo = "Machine", Frequency = "Daily", IsActive = true },
        new MaintenanceChecklist { ChecklistId = 12, ChecklistCode = "CHK-0002", ChecklistName = "Mold plan", AppliesTo = "Mold", IsActive = true },
    };
    public List<MaintenanceType> MaintenanceTypes { get; } = new()
    {
        new MaintenanceType { MaintenanceTypeId = 21, MaintenanceTypeCode = "MT-0001", MaintenanceTypeName = "Preventive", AppliesTo = "Machine", IsActive = true },
        new MaintenanceType { MaintenanceTypeId = 22, MaintenanceTypeCode = "MT-0002", MaintenanceTypeName = "Polish", AppliesTo = "Mold", IsActive = true },
    };
    public List<MachinePm> MachinePms { get; } = new();
    public List<MoldPm> MoldPms { get; } = new();

    public MaintenanceReportScenario()
    {
        Machine Mc(int id) => Machines.Single(m => m.MachineId == id);
        Mold Md(int id) => Molds.Single(m => m.MoldId == id);
        var daily = Checklists[0];
        var preventive = MaintenanceTypes[0];

        MachinePm Pm(int id, int machine, MaintenanceChecklist? plan, DateOnly due, DateOnly? done, string status, string? by = null,
            MaintenanceType? type = null, bool[]? items = null) => new()
        {
            MachinePmId = id, PmNo = $"MPM-000{id}", MachineId = machine, Machine = Mc(machine), ChecklistId = plan?.ChecklistId, Checklist = plan,
            MaintenanceTypeId = type?.MaintenanceTypeId, MaintenanceType = type, ScheduledDate = due, CompletedDate = done, Status = status,
            MaintenanceBy = by,
            ChecklistItems = (items ?? Array.Empty<bool>()).Select((c, i) => new MachinePmChecklistItem { MachinePmChecklistId = id * 10 + i, MachinePmId = id, SortOrder = i + 1, ItemLabel = $"Item {i + 1}", IsChecked = c }).ToList(),
        };

        MachinePms.Add(Pm(1, 1, daily, new(2026, 9, 20), new(2026, 9, 20), MachinePmStatus.Completed, "Ravi", items: new[] { true, false }));
        MachinePms.Add(Pm(2, 1, daily, new(2026, 9, 25), null, MachinePmStatus.Scheduled));
        MachinePms.Add(Pm(3, 2, null, new(2026, 10, 5), null, MachinePmStatus.Scheduled, type: preventive));
        MachinePms.Add(Pm(4, 2, null, new(2026, 9, 28), null, MachinePmStatus.InProgress));

        MoldPm P(int id, int mold, string category, DateOnly due, DateOnly? done, string status, int uas, int? threshold = null, int? interval = null,
            int? uac = null, string? by = null) => new()
        {
            MoldPmId = id, PmNo = $"MPMD-000{id}", MoldId = mold, Mold = Md(mold), Category = category, ScheduledDate = due, CompletedDate = done,
            Status = status, MoldUsageAtService = uas, ThresholdShots = threshold, IntervalShots = interval, UsageAtCompletion = uac, MaintenanceBy = by,
        };

        MoldPms.Add(P(1, 1, MoldPmCategory.ShotBased, new(2026, 9, 26), null, MoldPmStatus.Scheduled, 450_100, 450_000, 50_000));
        MoldPms.Add(P(2, 2, MoldPmCategory.ShotBased, new(2026, 9, 28), null, MoldPmStatus.Scheduled, 80_500, 80_000, 80_000));
        MoldPms.Add(P(3, 2, "Cleaning", new(2026, 9, 10), new(2026, 9, 12), MoldPmStatus.Completed, 85_000, uac: 85_000, by: "Kumar"));
        MoldPms.Add(P(4, 1, "Inspection", new(2026, 9, 27), null, MoldPmStatus.InProgress, 459_000));
    }
}

/// <summary>
/// IMaintenanceReportRepository over the scenario's lists, running the SAME MaintenanceReportQueryBuilder (and the same
/// Concat + Order) the EF repository translates to SQL. Honours cancellation like EF does.
/// </summary>
internal sealed class InMemoryMaintenanceReportRepository : IMaintenanceReportRepository
{
    private readonly MaintenanceReportScenario _s;

    public InMemoryMaintenanceReportRepository(MaintenanceReportScenario scenario)
    {
        _s = scenario;
    }

    public List<(string Method, MaintenanceReportScope? Scope, ReportPage? Page)> Calls { get; } = new();

    public Task<(IReadOnlyList<MaintenanceReportRow> Items, int TotalCount)> GetRowsAsync(
        MaintenanceReportQuery query, MaintenanceReportScope scope, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetRowsAsync), scope, page));

        var machine = MaintenanceReportQueryBuilder.IncludesMachinePms(query)
            ? MaintenanceReportQueryBuilder.FilterMachinePms(_s.MachinePms.AsQueryable(), query, scope, today)
            : null;
        var mold = MaintenanceReportQueryBuilder.IncludesMoldPms(query)
            ? MaintenanceReportQueryBuilder.FilterMoldPms(_s.MoldPms.AsQueryable(), query, scope, today)
            : null;

        var machineRows = machine?.Select(MaintenanceReportQueryBuilder.MachinePmRow(today));
        var moldRows = mold?.Select(MaintenanceReportQueryBuilder.MoldPmRow(today));
        var rows = machineRows is null ? moldRows : moldRows is null ? machineRows : machineRows.Concat(moldRows);
        if (rows is null)
            return Task.FromResult<(IReadOnlyList<MaintenanceReportRow>, int)>((Array.Empty<MaintenanceReportRow>(), 0));

        var ordered = MaintenanceReportQueryBuilder.Order(rows, query, scope).ToList();
        IReadOnlyList<MaintenanceReportRow> items = page is null ? ordered : ordered.Skip(page.Skip).Take(page.PageSize).ToList();
        return Task.FromResult((items, ordered.Count));
    }

    public Task<MaintenanceOverdueSummaryDto> GetOverdueSummaryAsync(MaintenanceReportQuery query, DateOnly today, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetOverdueSummaryAsync), MaintenanceReportScope.Overdue, null));
        var machine = MaintenanceReportQueryBuilder.IncludesMachinePms(query)
            ? MaintenanceReportQueryBuilder.FilterMachinePms(_s.MachinePms.AsQueryable(), query, MaintenanceReportScope.Overdue, today).Count() : 0;
        var mold = MaintenanceReportQueryBuilder.IncludesMoldPms(query)
            ? MaintenanceReportQueryBuilder.FilterMoldPms(_s.MoldPms.AsQueryable(), query, MaintenanceReportScope.Overdue, today).Count() : 0;
        return Task.FromResult(new MaintenanceOverdueSummaryDto { TotalOverdue = machine + mold, MachinePmOverdue = machine, MoldPmOverdue = mold });
    }

    public Task<MaintenanceReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetLookupsAsync), null, null));
        static ReportLookupItemDto Item(int id, string code, string name, bool active) => new() { Id = id, Code = code, Name = name, IsActive = active };

        return Task.FromResult(new MaintenanceReportLookupsDto
        {
            Machines = _s.Machines.Select(m => Item(m.MachineId, m.MachineCode, m.MachineName, m.IsActive)).ToList(),
            Molds = _s.Molds.Select(m => Item(m.MoldId, m.MoldCode, m.MoldName, m.Status != MoldStatus.Retired)).ToList(),
            MaintenancePlans = _s.Checklists.Where(c => c.AppliesTo == MaintenanceChecklistAppliesTo.Machine).Select(c => Item(c.ChecklistId, c.ChecklistCode, c.ChecklistName, c.IsActive)).ToList(),
            MaintenanceTypes = _s.MaintenanceTypes.Where(t => t.AppliesTo is MaintenanceTypeAppliesTo.Machine or MaintenanceTypeAppliesTo.Both).Select(t => Item(t.MaintenanceTypeId, t.MaintenanceTypeCode, t.MaintenanceTypeName, t.IsActive)).ToList(),
            Categories = MoldPmCategory.All,
            AssetTypes = MaintenanceReportAssetType.All,
            OpenStatuses = MaintenanceReportStatus.Open,
            Statuses = MaintenanceReportStatus.All,
        });
    }
}
