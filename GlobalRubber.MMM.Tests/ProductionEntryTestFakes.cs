using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Shared fakes for the production entry tests. Machines and products are the existing master fakes (machines 1, 2
/// active, 3 inactive; products 1, 2 active, 3 inactive). Molds are defined here so each life scenario is explicit
/// (warning 450,000 / replacement 500,000 / max 500,000):
///   1 MLD-0001 product 1, usage 100,000, Available          (well below warning)
///   2 MLD-0002 product 1, usage 440,000, Available          (crosses warning with 10,000+)
///   3 MLD-0003 product 2, usage 500,000, Replacement Due    (at the limit - blocked)
///   4 MLD-0004 product 1, usage 449,990, Retired            (Retired stays Retired in the warning band)
///   5 MLD-0005 product 3 (inactive), usage 0, Available
/// </summary>
internal static class ProductionEntryTestData
{
    public static List<Mold> Molds() => new()
    {
        Mold(1, "MLD-0001", 1, 100_000, MoldStatus.Available),
        Mold(2, "MLD-0002", 1, 440_000, MoldStatus.Available),
        Mold(3, "MLD-0003", 2, 500_000, MoldStatus.ReplacementDue),
        Mold(4, "MLD-0004", 1, 449_990, MoldStatus.Retired),
        Mold(5, "MLD-0005", 3, 0, MoldStatus.Available),
    };

    private static Mold Mold(int id, string code, int productId, int usage, string status) => new()
    {
        MoldId = id, MoldCode = code, MoldName = $"Mold {id}", ProductId = productId, MoldType = "Compression", CavityCount = 1,
        MaximumShots = 500_000, WarningShots = 450_000, ReplacementShots = 500_000, CurrentUsageShots = usage, Status = status,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
    };
}

/// <summary>
/// Behaves like the real ProductionEntryRepository where it matters: the callback runs on a COPY of the stored mold (the
/// "locked row"); only if it succeeds - and the simulated save does not fail - are the entry, the mold change and the
/// PROD-NNNN number committed together. A throw anywhere leaves entries, molds and the sequence untouched.
/// </summary>
internal sealed class InMemoryProductionEntryRepository : IProductionEntryRepository
{
    private readonly List<ProductionEntry> _entries = new();
    private readonly List<Mold> _molds;
    private readonly List<Machine> _machines;
    private readonly List<Product> _products;
    private int _lastNumber;

    public InMemoryProductionEntryRepository(List<Mold> molds, List<Machine> machines, List<Product> products)
    {
        _molds = molds;
        _machines = machines;
        _products = products;
    }

    /// <summary>Makes the next save fail after the callback, like a database error at SaveChanges.</summary>
    public bool FailNextSave { get; set; }
    public int AddCalls { get; private set; }
    public int Count => _entries.Count;
    public int LastNumber => _lastNumber;
    public ProductionEntry Stored(int id) => _entries.Single(e => e.ProductionEntryId == id);
    public Mold StoredMold(int id) => _molds.Single(m => m.MoldId == id);

    private ProductionEntry Clone(ProductionEntry e) => new()
    {
        ProductionEntryId = e.ProductionEntryId, EntryNo = e.EntryNo, EntryDate = e.EntryDate, Shift = e.Shift, MachineId = e.MachineId,
        ProductId = e.ProductId, MoldId = e.MoldId, ProductionQty = e.ProductionQty, RejectedQty = e.RejectedQty, GoodQty = e.GoodQty,
        MoldUsageBefore = e.MoldUsageBefore, MoldUsageAfter = e.MoldUsageAfter, Remarks = e.Remarks, Status = e.Status,
        CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy, UpdatedAt = e.UpdatedAt, UpdatedBy = e.UpdatedBy, RowVersion = (byte[])e.RowVersion.Clone(),
        Machine = _machines.Single(m => m.MachineId == e.MachineId),
        Product = _products.Single(p => p.ProductId == e.ProductId),
        Mold = _molds.Single(m => m.MoldId == e.MoldId),
    };

    public Task<(IReadOnlyList<ProductionEntry> Items, int TotalCount)> GetAllAsync(ProductionEntryListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<ProductionEntry> q = _entries;
        if (query.FromDate is { } from) q = q.Where(e => e.EntryDate >= from);
        if (query.ToDate is { } to) q = q.Where(e => e.EntryDate <= to);
        if (query.MachineId is { } machineId) q = q.Where(e => e.MachineId == machineId);
        if (query.MoldId is { } moldId) q = q.Where(e => e.MoldId == moldId);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) q = q.Where(e => e.EntryNo.Contains(search, StringComparison.OrdinalIgnoreCase));

        var all = q.OrderByDescending(e => e.ProductionEntryId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<ProductionEntry>, int)>((page, all.Count));
    }

    public Task<ProductionEntry?> GetByIdAsync(int productionEntryId, CancellationToken cancellationToken)
    {
        var stored = _entries.FirstOrDefault(e => e.ProductionEntryId == productionEntryId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<ProductionLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) => Task.FromResult(new ProductionLookupsDto
    {
        Machines = _machines.Where(m => m.IsActive).OrderBy(m => m.MachineCode)
            .Select(m => new ProductionMachineLookupDto { MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName, Location = m.Location }).ToList(),
        Products = _products.Where(p => p.IsActive).OrderBy(p => p.ProductCode)
            .Select(p => new ProductionProductLookupDto { ProductId = p.ProductId, ProductCode = p.ProductCode, ProductName = p.ProductName }).ToList(),
        Molds = _molds.Where(m => _products.Single(p => p.ProductId == m.ProductId).IsActive).OrderBy(m => m.MoldCode)
            .Select(m => new ProductionMoldLookupDto
            {
                MoldId = m.MoldId, MoldCode = m.MoldCode, MoldName = m.MoldName, ProductId = m.ProductId, CurrentUsageShots = m.CurrentUsageShots,
                MaximumShots = m.MaximumShots, WarningShots = m.WarningShots, ReplacementShots = m.ReplacementShots, Status = m.Status,
                LifeState = m.CurrentUsageShots >= m.ReplacementShots ? MoldLifeState.Replace : m.CurrentUsageShots >= m.WarningShots ? MoldLifeState.Warning : MoldLifeState.Normal,
            }).ToList(),
    });

    public async Task<ProductionEntry> AddAsync(
        ProductionEntry entry, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        AddCalls++;
        var stored = _molds.Single(m => m.MoldId == entry.MoldId);
        var locked = new Mold
        {
            MoldId = stored.MoldId, MoldCode = stored.MoldCode, MoldName = stored.MoldName, ProductId = stored.ProductId,
            MaximumShots = stored.MaximumShots, WarningShots = stored.WarningShots, ReplacementShots = stored.ReplacementShots,
            CurrentUsageShots = stored.CurrentUsageShots, Status = stored.Status, UpdatedAt = stored.UpdatedAt, UpdatedBy = stored.UpdatedBy,
            MaintenanceFrequencyShots = stored.MaintenanceFrequencyShots, PmWarningShots = stored.PmWarningShots, PmCycleStartShots = stored.PmCycleStartShots,
        };

        applyToLockedMold(locked); // may throw: nothing below happens

        if (FailNextSave)
        {
            FailNextSave = false;
            throw new DbUpdateException("Simulated database failure while saving the entry.");
        }

        // The in-transaction evaluation (automatic Mold PM) on the saved, still-locked mold; a throw = nothing committed.
        if (afterSave is not null)
        {
            await afterSave(locked, cancellationToken);
        }

        // "Commit": sequence, entry, mold - together.
        _lastNumber++;
        entry.ProductionEntryId = _entries.Count + 1;
        entry.EntryNo = $"PROD-{_lastNumber:0000}";
        entry.GoodQty = entry.ProductionQty - entry.RejectedQty; // what SQL Server's computed column returns
        entry.RowVersion = new byte[] { 1 };
        stored.CurrentUsageShots = locked.CurrentUsageShots;
        stored.Status = locked.Status;
        stored.UpdatedAt = locked.UpdatedAt;
        stored.UpdatedBy = locked.UpdatedBy;
        _entries.Add(Clone(entry));
        entry.Mold = stored;
        return entry;
    }
}
