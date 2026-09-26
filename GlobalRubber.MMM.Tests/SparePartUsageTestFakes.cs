using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Tests;

internal static class SparePartUsageTestData
{
    // Parts: 10 Bearing (Nos, stock 10, min 5, unit cost 250.00); 11 Grease (Kg, stock 5, min 2); 12 inactive Belt (stock 8).
    public static List<SparePart> Parts() => new()
    {
        Part(10, "SPR-0010", "Bearing 6204", "Nos", 10, 5, 250.00m),
        Part(11, "SPR-0011", "Grease EP2", "Kg", 5, 2, null),
        Part(12, "SPR-0012", "V-Belt A42", "Nos", 8, 1, 90m, active: false),
    };

    private static SparePart Part(int id, string code, string name, string unit, int stock, int min, decimal? cost, bool active = true) => new()
    {
        SparePartId = id, SparePartCode = code, SparePartName = name, Unit = unit, CurrentStock = stock, MinimumStock = min, UnitCost = cost,
        IsActive = active, StockStatus = stock <= 0 ? SparePartStockStatus.OutOfStock : stock <= min ? SparePartStockStatus.LowStock : SparePartStockStatus.Available,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), RowVersion = new byte[] { 1 },
    };

    // Machine PMs: 20 open & due (MPM-0010, MAC-0001), 21 completed, 22 open but scheduled in the future.
    public static List<(int Id, string No, string Status, DateOnly Date, int MachineId, string MachineCode)> MachinePms(DateOnly today) => new()
    {
        (20, "MPM-0010", MachinePmStatus.Scheduled, today.AddDays(-1), 1, "MAC-0001"),
        (21, "MPM-0011", MachinePmStatus.Completed, today.AddDays(-10), 1, "MAC-0001"),
        (22, "MPM-0012", MachinePmStatus.Scheduled, today.AddDays(30), 1, "MAC-0001"),
    };

    // Mold PMs: 30 open (MPMD-0005, MLD-0001), 31 completed.
    public static List<(int Id, string No, string Status, DateOnly Date, int MoldId, string MoldCode)> MoldPms(DateOnly today) => new()
    {
        (30, "MPMD-0005", MoldPmStatus.Scheduled, today, 1, "MLD-0001"),
        (31, "MPMD-0006", MoldPmStatus.Completed, today.AddDays(-5), 1, "MLD-0001"),
    };
}

/// <summary>
/// Behaves like the real SparePartUsageRepository where it matters: the rules run on a COPY of the stored spare part (the
/// "locked row"); only if they - and afterSave - succeed are the usage (SPU-NNNN), the stock and the ledger row committed
/// together; a throw leaves usages, stock, ledger and the sequence untouched. The request-id unique index and the usage
/// row version are enforced as in SQL Server. FailAfterUsageInsert / FailAfterStockUpdate simulate a failure mid-save.
/// </summary>
internal sealed class InMemorySparePartUsageRepository : ISparePartUsageRepository
{
    private readonly List<SparePart> _parts;
    private readonly DateOnly _today;
    private int _lastNumber;
    private byte _rv = 1;

    public InMemorySparePartUsageRepository(List<SparePart> parts, DateOnly today)
    {
        _parts = parts;
        _today = today;
        MachinePms = SparePartUsageTestData.MachinePms(today);
        MoldPms = SparePartUsageTestData.MoldPms(today);
    }

    public List<(int Id, string No, string Status, DateOnly Date, int MachineId, string MachineCode)> MachinePms { get; }
    public List<(int Id, string No, string Status, DateOnly Date, int MoldId, string MoldCode)> MoldPms { get; }
    public List<SparePartUsage> Usages { get; } = new();
    public List<SparePartStockTransaction> Ledger { get; } = new();
    public bool FailAfterUsageInsert { get; set; }
    public bool FailAfterStockUpdate { get; set; }

    public int SequenceNumber => _lastNumber;

    private SparePartUsage Copy(SparePartUsage u) => new()
    {
        SparePartUsageId = u.SparePartUsageId, UsageNo = u.UsageNo, SparePartId = u.SparePartId, Quantity = u.Quantity, UsageDate = u.UsageDate,
        UsedFor = u.UsedFor, ReferenceNo = u.ReferenceNo, MachineId = u.MachineId, MoldId = u.MoldId, MachinePmId = u.MachinePmId, MoldPmId = u.MoldPmId,
        UsedByEmployeeId = u.UsedByEmployeeId, UnitCostAtIssue = u.UnitCostAtIssue, Remarks = u.Remarks, Status = u.Status, ReversedAt = u.ReversedAt,
        ReversedBy = u.ReversedBy, ReversalReason = u.ReversalReason, RequestId = u.RequestId, CreatedAt = u.CreatedAt, CreatedBy = u.CreatedBy,
        UpdatedAt = u.UpdatedAt, UpdatedBy = u.UpdatedBy, RowVersion = u.RowVersion.ToArray(),
        SparePart = _parts.Single(p => p.SparePartId == u.SparePartId),
    };

    private static SparePart PartCopy(SparePart p) => new()
    {
        SparePartId = p.SparePartId, SparePartCode = p.SparePartCode, SparePartName = p.SparePartName, Unit = p.Unit, CurrentStock = p.CurrentStock,
        MinimumStock = p.MinimumStock, UnitCost = p.UnitCost, IsActive = p.IsActive, StockStatus = p.StockStatus, RowVersion = p.RowVersion,
    };

    public Task<(IReadOnlyList<SparePartUsage> Items, int TotalCount)> GetAllAsync(SparePartUsageListQuery query, CancellationToken cancellationToken)
    {
        var rows = Usages.Where(u => (query.SparePartId is null || u.SparePartId == query.SparePartId)
                                     && (query.DateFrom is null || u.UsageDate >= query.DateFrom) && (query.DateTo is null || u.UsageDate <= query.DateTo)
                                     && (query.MachineId is null || u.MachineId == query.MachineId) && (query.MoldId is null || u.MoldId == query.MoldId)
                                     && (string.IsNullOrWhiteSpace(query.MaintenanceType) || u.UsedFor == SparePartUsageMaintenanceType.UsedForOf(query.MaintenanceType))
                                     && (string.IsNullOrWhiteSpace(query.Status) || u.Status == query.Status)
                                     && (string.IsNullOrWhiteSpace(query.Search) || u.UsageNo.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                                         || (u.ReferenceNo ?? string.Empty).Contains(query.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(u => u.UsageDate).ThenByDescending(u => u.SparePartUsageId).ToList();
        IReadOnlyList<SparePartUsage> page = rows.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Copy).ToList();
        return Task.FromResult((page, rows.Count));
    }

    public Task<SparePartUsage?> GetByIdAsync(int sparePartUsageId, CancellationToken cancellationToken) =>
        Task.FromResult(Usages.Where(u => u.SparePartUsageId == sparePartUsageId).Select(Copy).FirstOrDefault());

    public Task<IReadOnlyList<SparePartStockTransaction>> GetStockMovementsAsync(int sparePartUsageId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SparePartStockTransaction>>(Ledger.Where(l => l.ReferenceType == SparePartStockReferenceType.SparePartUsage && l.ReferenceId == sparePartUsageId).ToList());

    public Task<SparePartUsageLookupsDto> GetLookupsAsync(DateOnly today, CancellationToken cancellationToken) => Task.FromResult(new SparePartUsageLookupsDto
    {
        SpareParts = _parts.Where(p => p.IsActive).Select(p => new UsageSparePartLookupDto
        {
            SparePartId = p.SparePartId, SparePartCode = p.SparePartCode, SparePartName = p.SparePartName, Unit = p.Unit, CurrentStock = p.CurrentStock,
            MinimumStock = p.MinimumStock, StockStatus = p.StockStatus,
        }).ToList(),
        MachinePms = MachinePms.Where(p => p.Status != MachinePmStatus.Completed && p.Date <= today)
            .Select(p => new UsageMaintenanceLookupDto { MaintenanceId = p.Id, MaintenanceNo = p.No, AssetCode = p.MachineCode, ScheduledDate = p.Date, Status = p.Status }).ToList(),
        MoldPms = MoldPms.Where(p => p.Status != MoldPmStatus.Completed)
            .Select(p => new UsageMaintenanceLookupDto { MaintenanceId = p.Id, MaintenanceNo = p.No, AssetCode = p.MoldCode, ScheduledDate = p.Date, Status = p.Status }).ToList(),
    });

    public Task<SparePartUsage?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Usages.Where(u => u.RequestId == requestId).Select(Copy).FirstOrDefault());

    public async Task<SparePartUsage> AddAsync(
        SparePartUsage usage, string maintenanceType, int maintenanceId,
        Func<SparePart, MaintenanceReference?, SparePartStockTransaction> applyToLocked,
        Func<SparePart, SparePartUsage, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        var stored = _parts.SingleOrDefault(p => p.SparePartId == usage.SparePartId) ?? throw new NotFoundException(nameof(SparePart), usage.SparePartId);
        var locked = PartCopy(stored);

        MaintenanceReference? pm = null;
        if (maintenanceType == SparePartUsageMaintenanceType.MachinePm && MachinePms.FirstOrDefault(p => p.Id == maintenanceId) is { Id: > 0 } m)
            pm = new MaintenanceReference(m.Id, m.No, m.Status, m.Date, m.MachineId, null, m.MachineCode);
        if (maintenanceType == SparePartUsageMaintenanceType.MoldPm && MoldPms.FirstOrDefault(p => p.Id == maintenanceId) is { Id: > 0 } d)
            pm = new MaintenanceReference(d.Id, d.No, d.Status, d.Date, null, d.MoldId, d.MoldCode);

        var ledger = applyToLocked(locked, pm); // may throw: nothing written

        if (usage.RequestId is { } requestId && Usages.Any(u => u.RequestId == requestId))
        {
            throw new DuplicateRequestException(requestId); // UX_spare_part_usage_transaction_request_id
        }

        // Simulated failures INSIDE the transaction: the whole thing rolls back.
        if (FailAfterUsageInsert) throw new DbUpdateException("Simulated failure after the usage insert.");
        if (FailAfterStockUpdate) throw new DbUpdateException("Simulated failure after the stock update.");

        var number = _lastNumber + 1;
        usage.SparePartUsageId = Usages.Count + 1;
        usage.UsageNo = $"SPU-{number:0000}";
        usage.RowVersion = new[] { _rv++ };
        ledger.ReferenceId = usage.SparePartUsageId;
        ledger.ReferenceNo = $"{usage.UsageNo} ({usage.ReferenceNo})";

        await (afterSave?.Invoke(locked, usage, cancellationToken) ?? Task.CompletedTask); // may throw: nothing committed

        // "Commit".
        _lastNumber = number;
        Usages.Add(Copy(usage));
        Ledger.Add(ledger);
        stored.CurrentStock = locked.CurrentStock;
        stored.StockStatus = locked.CurrentStock <= 0 ? SparePartStockStatus.OutOfStock : locked.CurrentStock <= locked.MinimumStock ? SparePartStockStatus.LowStock : SparePartStockStatus.Available;
        stored.RowVersion = new[] { _rv++ };
        usage.SparePart = stored;
        return usage;
    }

    public Task<SparePartUsage> ReverseAsync(
        int sparePartUsageId, byte[] originalRowVersion, Func<SparePartUsage, SparePart, SparePartStockTransaction> applyToLocked, CancellationToken cancellationToken)
    {
        var storedUsage = Usages.SingleOrDefault(u => u.SparePartUsageId == sparePartUsageId) ?? throw new NotFoundException(nameof(SparePartUsage), sparePartUsageId);
        var storedPart = _parts.Single(p => p.SparePartId == storedUsage.SparePartId);
        var usage = Copy(storedUsage);
        var part = PartCopy(storedPart);

        var ledger = applyToLocked(usage, part);
        if (!storedUsage.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The spare part usage was modified by another user. Refresh it and try again.");
        }

        Usages[Usages.IndexOf(storedUsage)] = usage;
        usage.RowVersion = new[] { _rv++ };
        storedPart.CurrentStock = part.CurrentStock;
        Ledger.Add(ledger);
        usage.SparePart = storedPart;
        return Task.FromResult(usage);
    }
}
