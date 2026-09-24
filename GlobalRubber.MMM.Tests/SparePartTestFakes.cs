using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Shared fakes for the spare part tests. Machines (<see cref="MachineTestData"/>): 1, 2 active, 3 inactive. Vendors
/// (<see cref="VendorTestData"/>): 1, 2 active, 3 inactive.
/// </summary>
internal static class SparePartTestData
{
    // 1 Hydraulic Seal Kit (machine 1, vendor 1, 12 / min 5 = Available)
    // 2 Heater Band (machine 3 = inactive, vendor 3 = inactive, 2 / min 5 = Low Stock)
    // 3 Old Bearing (inactive, no machine/vendor, 0 = Out of Stock)
    public static List<SparePart> SpareParts() => new()
    {
        new SparePart
        {
            SparePartId = 1, SparePartCode = "SPR-0001", SparePartName = "Hydraulic Seal Kit", Category = "Hydraulics", MachineId = 1,
            PartNumber = "HSK-100", Unit = "Nos", MinimumStock = 5, CurrentStock = 12, VendorId = 1, StoreLocation = "Rack S-1",
            UnitCost = 1250.50m, StockStatus = "Available", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new SparePart
        {
            SparePartId = 2, SparePartCode = "SPR-0002", SparePartName = "Heater Band", MachineId = 3, Unit = "Nos",
            MinimumStock = 5, CurrentStock = 2, VendorId = 3, StockStatus = "Low Stock", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new SparePart
        {
            SparePartId = 3, SparePartCode = "SPR-0003", SparePartName = "Old Bearing", Unit = "Nos", MinimumStock = 0,
            CurrentStock = 0, StockStatus = "Out of Stock", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };

    /// <summary>The persisted computed column stock_status, exactly as the DDL defines it (BR-29).</summary>
    public static string StockStatusOf(SparePart s) =>
        s.CurrentStock <= 0 ? SparePartStockStatus.OutOfStock
        : s.CurrentStock <= s.MinimumStock ? SparePartStockStatus.LowStock
        : SparePartStockStatus.Available;
}

/// <summary>
/// Behaves like the real SparePartRepository where it matters: detached copies on read WITH Machine and Vendor loaded, next
/// SPR-NNNN code on add, StockStatus recomputed on every write (as SQL Server does), Update/Deactivate write only the
/// columns the real ones write - and only if the row version still matches.
/// </summary>
internal sealed class InMemorySparePartRepository : ISparePartRepository
{
    private readonly List<SparePart> _parts;
    private readonly InMemoryMachineRepository _machines;
    private readonly InMemoryVendorRepository _vendors;
    private int _nextCode;

    public InMemorySparePartRepository(List<SparePart> parts, InMemoryMachineRepository machines, InMemoryVendorRepository vendors)
    {
        _parts = parts;
        _machines = machines;
        _vendors = vendors;
        _nextCode = parts.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _parts.Count;

    public SparePart Stored(int id) => _parts.Single(s => s.SparePartId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static SparePart CopyColumns(SparePart s) => new()
    {
        SparePartId = s.SparePartId, SparePartCode = s.SparePartCode, SparePartName = s.SparePartName, Category = s.Category,
        MachineId = s.MachineId, PartNumber = s.PartNumber, Unit = s.Unit, MinimumStock = s.MinimumStock, CurrentStock = s.CurrentStock,
        VendorId = s.VendorId, StoreLocation = s.StoreLocation, UnitCost = s.UnitCost, StockStatus = s.StockStatus, IsActive = s.IsActive,
        CreatedAt = s.CreatedAt, CreatedBy = s.CreatedBy, UpdatedAt = s.UpdatedAt, UpdatedBy = s.UpdatedBy,
        RowVersion = (byte[])s.RowVersion.Clone(),
    };

    private SparePart Clone(SparePart s)
    {
        var copy = CopyColumns(s);
        copy.Machine = s.MachineId is { } machineId ? _machines.Stored(machineId) : null;
        copy.Vendor = s.VendorId is { } vendorId ? _vendors.Stored(vendorId) : null;
        return copy;
    }

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<SparePart> Items, int TotalCount)> GetAllAsync(SparePartListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<SparePart> q = _parts;
        if (query.IsActive is { } isActive) q = q.Where(s => s.IsActive == isActive);
        if (!string.IsNullOrWhiteSpace(query.StockStatus)) q = q.Where(s => s.StockStatus == query.StockStatus.Trim());
        if (query.MachineId is { } machineId) q = q.Where(s => s.MachineId == machineId);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(s => s.SparePartCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || s.SparePartName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(s => s.SparePartName).ThenBy(s => s.SparePartId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<SparePart>, int)>((page, all.Count));
    }

    public Task<SparePart?> GetByIdAsync(int sparePartId, CancellationToken cancellationToken)
    {
        var stored = _parts.FirstOrDefault(s => s.SparePartId == sparePartId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeSparePartId, CancellationToken cancellationToken) =>
        Task.FromResult(_parts.Any(s =>
            s.IsActive && s.SparePartId != excludeSparePartId && string.Equals(s.SparePartName, name, StringComparison.OrdinalIgnoreCase)));

    public Task<SparePart> AddAsync(SparePart sparePart, CancellationToken cancellationToken)
    {
        AddCalls++;
        sparePart.SparePartId = _parts.Max(s => s.SparePartId) + 1;
        sparePart.SparePartCode = $"SPR-{_nextCode++:0000}"; // the real repository issues this from the SPARE_PART sequence
        sparePart.RowVersion = new byte[] { 1 };
        sparePart.StockStatus = SparePartTestData.StockStatusOf(sparePart);
        _parts.Add(CopyColumns(sparePart));
        return Task.FromResult(sparePart);
    }

    public Task<SparePart> UpdateAsync(SparePart sparePart, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(sparePart.SparePartId);

        var stored = _parts.FirstOrDefault(s => s.SparePartId == sparePart.SparePartId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The spare part was modified by another user. Refresh the spare part and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never the code, IsActive or the creation columns.
        stored.SparePartName = sparePart.SparePartName;
        stored.Category = sparePart.Category;
        stored.MachineId = sparePart.MachineId;
        stored.PartNumber = sparePart.PartNumber;
        stored.Unit = sparePart.Unit;
        stored.MinimumStock = sparePart.MinimumStock;
        stored.CurrentStock = sparePart.CurrentStock;
        stored.VendorId = sparePart.VendorId;
        stored.StoreLocation = sparePart.StoreLocation;
        stored.UnitCost = sparePart.UnitCost;
        stored.UpdatedAt = sparePart.UpdatedAt;
        stored.UpdatedBy = sparePart.UpdatedBy;
        stored.StockStatus = SparePartTestData.StockStatusOf(stored);
        stored.RowVersion = Next(stored.RowVersion);
        sparePart.RowVersion = stored.RowVersion;
        sparePart.StockStatus = stored.StockStatus;
        return Task.FromResult(sparePart);
    }

    public Task<SparePart> DeactivateAsync(SparePart sparePart, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(sparePart.SparePartId);

        var stored = _parts.FirstOrDefault(s => s.SparePartId == sparePart.SparePartId);
        if (stored is null || !stored.RowVersion.SequenceEqual(sparePart.RowVersion))
        {
            throw new ConflictException("The spare part was modified by another user. Refresh the spare part and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = sparePart.UpdatedAt;
        stored.UpdatedBy = sparePart.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        sparePart.RowVersion = stored.RowVersion;
        return Task.FromResult(sparePart);
    }
}
