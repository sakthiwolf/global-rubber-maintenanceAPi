using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// A fixed Mold Reports scenario (plant "today" = 2026-09-28). LifeState is set as SQL Server's computed column would.
///
///   Molds (usage / max / warning / replacement, status, PM interval):
///     MLD-0001 Cap Mold A    P1 Injection  460,000/500,000/450,000/480,000 Warning  In Production    50,000 (cycle 450,000) -> 92%
///     MLD-0002 Cap Mold B    P1 Injection  490,000/500,000/450,000/480,000 Replace  Replacement Due  none                   -> 98%
///     MLD-0003 Bottle Mold   P2 Blow        10,000/500,000/450,000/480,000 Normal   Available        50,000 (cycle 0)       ->  2%
///     MLD-0004 Old Mold      P2 Blow       300,000/400,000/350,000/380,000 Normal   Retired          none                   -> 75%
///   Production: E1 09-01 MAC-0001 MLD-0001 1,000 | E2 09-10 MAC-0002 MLD-0001 500 | E3 09-15 MAC-0001 MLD-0003 2,000 |
///               E4 09-20 MAC-0002 MLD-0003 700 CANCELLED
///   Mold PMs:   MPMD-0001 MLD-0001 Shot-based due 09-05 done 09-06 by Ravi (threshold 450,000, usage 451,000)
///               MPMD-0002 MLD-0003 Shot-based due 09-20, Scheduled -> Overdue
///               MPMD-0003 MLD-0001 Cleaning   due 09-28, Scheduled -> Due
///               MPMD-0004 MLD-0002 Shot-based due 09-25, In Progress
///   => life: 4 molds, 2 Normal, 1 Warning, 1 Replace, 1 Retired, 1 replacement required (MLD-0002).
/// </summary>
internal sealed class MoldReportScenario
{
    public static readonly DateOnly Today = new(2026, 9, 28);

    public List<Product> Products { get; } = new()
    {
        new Product { ProductId = 1, ProductCode = "PRD-0001", ProductName = "Cap", IsActive = true },
        new Product { ProductId = 2, ProductCode = "PRD-0002", ProductName = "Bottle", IsActive = true },
        new Product { ProductId = 3, ProductCode = "PRD-0003", ProductName = "Unused", IsActive = true },
    };

    public List<Machine> Machines { get; } = MachineTestData.Machines();
    public List<Mold> Molds { get; } = new();
    public List<ProductionEntry> Entries { get; } = new();
    public List<MoldPm> Pms { get; } = new();

    public MoldReportScenario()
    {
        Mold M(int id, string code, string name, int product, string type, int usage, int max, int warn, int repl, string life, string status,
            int? interval, int cycle, int? pmWarn = null, string? location = null) => new()
        {
            MoldId = id, MoldCode = code, MoldName = name, ProductId = product, Product = Products.Single(p => p.ProductId == product),
            MoldType = type, CavityCount = 1, CurrentUsageShots = usage, MaximumShots = max, WarningShots = warn, ReplacementShots = repl,
            LifeState = life, Status = status, MaintenanceFrequencyShots = interval, PmCycleStartShots = cycle, PmWarningShots = pmWarn,
            Location = location,
        };

        Molds.Add(M(1, "MLD-0001", "Cap Mold A", 1, "Injection", 460_000, 500_000, 450_000, 480_000, MoldLifeState.Warning, MoldStatus.InProduction, 50_000, 450_000, 5_000, "Bay 1"));
        Molds.Add(M(2, "MLD-0002", "Cap Mold B", 1, "Injection", 490_000, 500_000, 450_000, 480_000, MoldLifeState.Replace, MoldStatus.ReplacementDue, null, 0));
        Molds.Add(M(3, "MLD-0003", "Bottle Mold", 2, "Blow", 10_000, 500_000, 450_000, 480_000, MoldLifeState.Normal, MoldStatus.Available, 50_000, 0));
        Molds.Add(M(4, "MLD-0004", "Old Mold", 2, "Blow", 300_000, 400_000, 350_000, 380_000, MoldLifeState.Normal, MoldStatus.Retired, null, 0));

        ProductionEntry E(int id, DateOnly date, int machine, int mold, int qty, string status = ProductionEntryStatus.Saved) => new()
        {
            ProductionEntryId = id, EntryNo = $"PROD-000{id}", EntryDate = date, MachineId = machine, Machine = Machines.Single(m => m.MachineId == machine),
            MoldId = mold, Mold = Molds.Single(m => m.MoldId == mold), ProductId = 1, ProductionQty = qty, Status = status, Shift = "Shift A",
        };

        Entries.Add(E(1, new(2026, 9, 1), 1, 1, 1_000));
        Entries.Add(E(2, new(2026, 9, 10), 2, 1, 500));
        Entries.Add(E(3, new(2026, 9, 15), 1, 3, 2_000));
        Entries.Add(E(4, new(2026, 9, 20), 2, 3, 700, ProductionEntryStatus.Cancelled));

        MoldPm P(int id, int mold, string category, DateOnly due, DateOnly? done, string status, int uas, int? threshold = null, int? interval = null,
            int? uac = null, string? by = null) => new()
        {
            MoldPmId = id, PmNo = $"MPMD-000{id}", MoldId = mold, Mold = Molds.Single(m => m.MoldId == mold), Category = category,
            ScheduledDate = due, CompletedDate = done, Status = status, MoldUsageAtService = uas, ThresholdShots = threshold,
            IntervalShots = interval, UsageAtCompletion = uac, MaintenanceBy = by,
        };

        Pms.Add(P(1, 1, MoldPmCategory.ShotBased, new(2026, 9, 5), new(2026, 9, 6), MoldPmStatus.Completed, 450_100, 450_000, 50_000, 451_000, "Ravi"));
        Pms.Add(P(2, 3, MoldPmCategory.ShotBased, new(2026, 9, 20), null, MoldPmStatus.Scheduled, 10_000, 50_000, 50_000));
        Pms.Add(P(3, 1, "Cleaning", new(2026, 9, 28), null, MoldPmStatus.Scheduled, 460_000));
        Pms.Add(P(4, 2, MoldPmCategory.ShotBased, new(2026, 9, 25), null, MoldPmStatus.InProgress, 490_000, 450_000, 50_000));
    }
}

/// <summary>IMoldReportRepository over the scenario's lists, running the SAME MoldReportQueryBuilder the EF repository translates.</summary>
internal sealed class InMemoryMoldReportRepository : IMoldReportRepository
{
    private readonly MoldReportScenario _s;

    public InMemoryMoldReportRepository(MoldReportScenario scenario)
    {
        _s = scenario;
    }

    public List<(string Method, ReportPage? Page)> Calls { get; } = new();

    public Task<(IReadOnlyList<MoldListRow> Items, int TotalCount)> GetMoldListAsync(MoldListReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetMoldListAsync), page));
        var filtered = MoldReportQueryBuilder.FilterMolds(_s.Molds.AsQueryable(), query);
        return Task.FromResult(Page(filtered, MoldReportQueryBuilder.OrderByCode(filtered).Select(MoldReportQueryBuilder.MoldListRow(_s.Pms.AsQueryable())), page));
    }

    public Task<(IReadOnlyList<MoldUsageRow> Items, int TotalCount)> GetUsageAsync(MoldUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetUsageAsync), page));
        var scoped = MoldReportQueryBuilder.ScopedProduction(_s.Entries.AsQueryable(), query);
        var filtered = MoldReportQueryBuilder.FilterUsage(_s.Molds.AsQueryable(), scoped, query);
        return Task.FromResult(Page(filtered, MoldReportQueryBuilder.OrderUsage(filtered.Select(MoldReportQueryBuilder.MoldUsageRow(scoped)), query), page));
    }

    public Task<(IReadOnlyList<MoldBaseRow> Items, int TotalCount)> GetLifeAsync(MoldLifeReportQuery query, bool replacementScope, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetLifeAsync), page));
        var filtered = MoldReportQueryBuilder.FilterLife(_s.Molds.AsQueryable(), query, replacementScope);
        return Task.FromResult(Page(filtered, MoldReportQueryBuilder.OrderLife(filtered, query).Select(MoldReportQueryBuilder.MoldBaseRow), page));
    }

    public Task<MoldLifeSummaryDto> GetLifeSummaryAsync(MoldLifeReportQuery query, bool replacementScope, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetLifeSummaryAsync), null));
        var filtered = MoldReportQueryBuilder.FilterLife(_s.Molds.AsQueryable(), query, replacementScope);
        return Task.FromResult(MoldReportQueryBuilder.LifeSummary(filtered).FirstOrDefault() ?? new MoldLifeSummaryDto());
    }

    public Task<(IReadOnlyList<MoldMaintenanceReportItemDto> Items, int TotalCount)> GetMaintenanceAsync(
        MoldMaintenanceReportQuery query, DateOnly today, ReportPage? page, CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetMaintenanceAsync), page));
        var filtered = MoldReportQueryBuilder.FilterMaintenance(_s.Pms.AsQueryable(), query, today);
        return Task.FromResult(Page(filtered, MoldReportQueryBuilder.OrderMaintenance(filtered).Select(MoldReportQueryBuilder.MaintenanceRow(today)), page));
    }

    public Task<MoldReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        Calls.Add((nameof(GetLookupsAsync), null));
        static ReportLookupItemDto Item(int id, string code, string name, bool active) => new() { Id = id, Code = code, Name = name, IsActive = active };

        return Task.FromResult(new MoldReportLookupsDto
        {
            Molds = _s.Molds.OrderBy(m => m.MoldCode).Select(m => Item(m.MoldId, m.MoldCode, m.MoldName, m.Status != MoldStatus.Retired)).ToList(),
            Products = _s.Products.Where(p => _s.Molds.Any(m => m.ProductId == p.ProductId)).Select(p => Item(p.ProductId, p.ProductCode, p.ProductName, p.IsActive)).ToList(),
            Machines = _s.Machines.Where(mc => _s.Entries.Any(e => e.MachineId == mc.MachineId)).Select(mc => Item(mc.MachineId, mc.MachineCode, mc.MachineName, mc.IsActive)).ToList(),
            MoldTypes = _s.Molds.Select(m => m.MoldType).Distinct().OrderBy(t => t).ToList(),
            Statuses = MoldStatus.All,
            LifeStates = MoldLifeState.All,
            Categories = MoldPmCategory.All,
            MaintenanceStatuses = MoldPmBucket.All,
            ReplacementStatuses = MoldReplacementStatus.All,
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
