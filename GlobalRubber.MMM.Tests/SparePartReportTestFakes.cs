using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// A fixed Spare Part Reports scenario (plant "today" = 2026-09-28). StockStatus is set as SQL Server's computed column would.
///   Parts: SPR-0001 Bearing  Bearings   Nos min 5 cur 0  cost 10.00 vendor VND-0001 machine MAC-0001  -> Out of Stock
///          SPR-0002 Grease   Lubricants Kg  min 5 cur 3  no cost                                     -> Low Stock
///          SPR-0003 Belt     Belts      Nos min 5 cur 20 cost 2.50  INACTIVE                         -> Available (no ledger)
///          SPR-0004 Seal     Belts      Nos min 2 cur 8  cost 1.00  (ledger says 7 -> Mismatch)      -> Available
///   Ledger (UTC): #1 SPR-0001 Opening +10 09-01 20:00 (IST 09-02 01:30) | #2 SPR-0002 Opening +3 09-04 18:40 (IST 09-05 00:10)
///                 #3 SPR-0004 Opening +7 09-03 10:00 | #4 SPR-0001 Issue -10 (10 -> 0) 09-05 03:00 (IST 08:30) "SPU-0001 (MPM-0001)"
///   Usage: SPU-0001 SPR-0001 x4 09-05 Machine Maintenance MAC-0002 MPM-0001, cost at issue 12.50 (master now 10.00), Posted, Ravi
///          SPU-0002 SPR-0002 x2 09-06 Mold Maintenance MLD-0001 MPMD-0001, no cost, Reversed 09-06 04:00 UTC "Wrong part"
///   => valuation 50.00 + 8.00 + 0.00 = 58.00 (1 part without cost); usage cost 4 x 12.50 = 50.00.
/// </summary>
internal sealed class SparePartReportScenario
{
    public static readonly DateOnly Today = new(2026, 9, 28);

    public List<Machine> Machines { get; } = MachineTestData.Machines();
    public List<Vendor> Vendors { get; } = new() { new Vendor { VendorId = 1, VendorCode = "VND-0001", VendorName = "Acme Bearings", IsActive = true } };
    public List<User> Users { get; } = new() { new User { UserId = 1, LoginId = "admin", UserName = "Admin User" } };
    public List<SparePart> Parts { get; } = new();
    public List<SparePartStockTransaction> Ledger { get; } = new();
    public List<SparePartUsage> Usages { get; } = new();

    public SparePartReportScenario()
    {
        SparePart P(int id, string name, string category, string unit, int min, int cur, decimal? cost, string status, bool active = true,
            int? vendorId = null, int? machineId = null) => new()
        {
            SparePartId = id, SparePartCode = $"SPR-000{id}", SparePartName = name, Category = category, Unit = unit, MinimumStock = min,
            CurrentStock = cur, UnitCost = cost, StockStatus = status, IsActive = active,
            VendorId = vendorId, Vendor = Vendors.SingleOrDefault(v => v.VendorId == vendorId),
            MachineId = machineId, Machine = Machines.SingleOrDefault(m => m.MachineId == machineId),
        };

        Parts.Add(P(1, "Bearing", "Bearings", "Nos", 5, 0, 10.00m, SparePartStockStatus.OutOfStock, vendorId: 1, machineId: 1));
        Parts.Add(P(2, "Grease", "Lubricants", "Kg", 5, 3, null, SparePartStockStatus.LowStock));
        Parts.Add(P(3, "Belt", "Belts", "Nos", 5, 20, 2.50m, SparePartStockStatus.Available, active: false));
        Parts.Add(P(4, "Seal", "Belts", "Nos", 2, 8, 1.00m, SparePartStockStatus.Available));

        SparePartStockTransaction L(long id, int part, string type, int qty, int before, DateTime at, string? reference, int? by = 1) => new()
        {
            StockTransactionId = id, SparePartId = part, TransactionType = type, Quantity = qty, PreviousStock = before, NewStock = before + qty,
            TransactionAt = at, ReferenceNo = reference, ReferenceType = type == SparePartStockTransactionType.Issue ? "SparePartUsage" : "SparePart", CreatedBy = by,
        };

        Ledger.Add(L(1, 1, SparePartStockTransactionType.Opening, 10, 0, new(2026, 9, 1, 20, 0, 0), "SPR-0001"));
        Ledger.Add(L(2, 2, SparePartStockTransactionType.Opening, 3, 0, new(2026, 9, 4, 18, 40, 0), "SPR-0002"));
        Ledger.Add(L(3, 4, SparePartStockTransactionType.Opening, 7, 0, new(2026, 9, 3, 10, 0, 0), "SPR-0004", by: null));
        Ledger.Add(L(4, 1, SparePartStockTransactionType.Issue, -10, 10, new(2026, 9, 5, 3, 0, 0), "SPU-0001 (MPM-0001)"));

        var mold = new Mold { MoldId = 1, MoldCode = "MLD-0001", MoldName = "Cap Mold", Status = MoldStatus.InProduction };
        Usages.Add(new SparePartUsage
        {
            SparePartUsageId = 1, UsageNo = "SPU-0001", SparePartId = 1, SparePart = Parts[0], Quantity = 4, UsageDate = new(2026, 9, 5),
            UsedFor = SparePartUsageMaintenanceType.UsedForMachineMaintenance, MachineId = 2, Machine = Machines.Single(m => m.MachineId == 2),
            MachinePmId = 204, MachinePm = new MachinePm { MachinePmId = 204, PmNo = "MPM-0001" },
            UsedByEmployeeId = 3, UsedByEmployee = new Employee { EmployeeId = 3, EmployeeName = "Ravi" },
            UnitCostAtIssue = 12.50m, Status = SparePartUsageStatus.Posted, CreatedAt = new(2026, 9, 5, 3, 0, 0), CreatedBy = 1,
        });
        Usages.Add(new SparePartUsage
        {
            SparePartUsageId = 2, UsageNo = "SPU-0002", SparePartId = 2, SparePart = Parts[1], Quantity = 2, UsageDate = new(2026, 9, 6),
            UsedFor = SparePartUsageMaintenanceType.UsedForMoldMaintenance, MoldId = 1, Mold = mold, MoldPmId = 1, MoldPm = new MoldPm { MoldPmId = 1, PmNo = "MPMD-0001" },
            UnitCostAtIssue = null, Status = SparePartUsageStatus.Reversed, ReversedAt = new(2026, 9, 6, 4, 0, 0), ReversalReason = "Wrong part",
            CreatedAt = new(2026, 9, 6, 3, 0, 0), CreatedBy = 1,
        });
    }
}

/// <summary>
/// ISparePartReportRepository over the scenario's lists, running the SAME SparePartReportQueryBuilder the EF repository
/// translates to SQL. Honours cancellation like EF does.
/// </summary>
internal sealed class InMemorySparePartReportRepository : ISparePartReportRepository
{
    private readonly SparePartReportScenario _s;

    public InMemorySparePartReportRepository(SparePartReportScenario scenario)
    {
        _s = scenario;
    }

    public List<(string Method, ReportPage? Page)> Calls { get; } = new();

    public Task<(IReadOnlyList<SparePartStockItemDto> Items, int TotalCount)> GetStockAsync(
        SparePartStockReportQuery query, SparePartStockScope scope, ReportPage? page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetStockAsync), page));
        var filtered = SparePartReportQueryBuilder.FilterParts(_s.Parts.AsQueryable(), query, scope);
        var rows = SparePartReportQueryBuilder.OrderParts(filtered, query, scope).Select(SparePartReportQueryBuilder.StockRow(_s.Ledger.AsQueryable()));
        return Task.FromResult(Page(filtered, rows, page));
    }

    public Task<SparePartLowStockSummaryDto> GetLowStockSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filtered = SparePartReportQueryBuilder.FilterParts(_s.Parts.AsQueryable(), query, SparePartStockScope.LowStock);
        return Task.FromResult(SparePartReportQueryBuilder.LowStockSummary(filtered).FirstOrDefault() ?? new SparePartLowStockSummaryDto());
    }

    public Task<SparePartValuationSummaryDto> GetValuationSummaryAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filtered = SparePartReportQueryBuilder.FilterParts(_s.Parts.AsQueryable(), query, SparePartStockScope.Valuation);
        return Task.FromResult(SparePartReportQueryBuilder.ValuationSummary(filtered).FirstOrDefault() ?? new SparePartValuationSummaryDto());
    }

    public Task<(IReadOnlyList<SparePartMovementItemDto> Items, int TotalCount)> GetMovementsAsync(
        SparePartMovementReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetMovementsAsync), page));
        var ledger = SparePartReportQueryBuilder.FilterLedger(_s.Ledger.AsQueryable(), query);
        var rows = SparePartReportQueryBuilder.MovementRows(ledger, _s.Parts.AsQueryable(), _s.Users.AsQueryable(), query);
        var items = SparePartReportQueryBuilder.OrderMovements(rows, query).Select(SparePartReportQueryBuilder.MovementItem);
        return Task.FromResult(Page(rows, items, page));
    }

    public Task<(IReadOnlyList<SparePartUsageItemDto> Items, int TotalCount)> GetUsageAsync(
        SparePartUsageReportQuery query, ReportPage? page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetUsageAsync), page));
        var filtered = SparePartReportQueryBuilder.FilterUsage(_s.Usages.AsQueryable(), query);
        var rows = SparePartReportQueryBuilder.OrderUsage(filtered, query).Select(SparePartReportQueryBuilder.UsageRow(_s.Users.AsQueryable()));
        return Task.FromResult(Page(filtered, rows, page));
    }

    public Task<SparePartReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((nameof(GetLookupsAsync), null));
        static ReportLookupItemDto Item(int id, string code, string name, bool active) => new() { Id = id, Code = code, Name = name, IsActive = active };

        return Task.FromResult(new SparePartReportLookupsDto
        {
            SpareParts = _s.Parts.Select(p => Item(p.SparePartId, p.SparePartCode, p.SparePartName, p.IsActive)).ToList(),
            Machines = _s.Machines.Where(m => _s.Parts.Any(p => p.MachineId == m.MachineId) || _s.Usages.Any(u => u.MachineId == m.MachineId))
                .Select(m => Item(m.MachineId, m.MachineCode, m.MachineName, m.IsActive)).ToList(),
            Molds = _s.Usages.Where(u => u.Mold != null).Select(u => Item(u.Mold!.MoldId, u.Mold.MoldCode, u.Mold.MoldName, true)).ToList(),
            Vendors = _s.Vendors.Select(v => Item(v.VendorId, v.VendorCode, v.VendorName, v.IsActive)).ToList(),
            Categories = _s.Parts.Where(p => p.Category != null).Select(p => p.Category!).Distinct().OrderBy(c => c).ToList(),
            StockStatuses = SparePartStockStatus.All,
            AttentionStatuses = SparePartReportLists.AttentionStatuses,
            TransactionTypes = SparePartReportLists.TransactionTypes,
            Directions = SparePartMovementDirection.All,
            UsageStatuses = SparePartReportLists.UsageStatuses,
            UsedFor = SparePartReportLists.UsedFor,
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
