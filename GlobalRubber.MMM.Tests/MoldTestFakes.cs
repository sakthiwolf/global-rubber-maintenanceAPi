using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Shared fakes for the mold tests. Products (<see cref="ProductTestData"/>): 1, 2 active, 3 inactive. Employees
/// (<see cref="EmployeeTestData"/>): 1, 2 active, 3 inactive.
/// </summary>
internal static class MoldTestData
{
    // 1 Seal Mold A (product 1, person 1, serial, 100k/500k = Normal, In Production; PM every 50k, cycle from 100k as the
    //   migration 014 backfill sets it - next PM threshold 150k)
    // 2 Gasket Mold (product 3 = inactive, person 3 = inactive, 460k = Warning, Available)
    // 3 Old Mold (Retired, product 2, 500k = Replace)
    public static List<Mold> Molds() => new()
    {
        new Mold
        {
            MoldId = 1, MoldCode = "MLD-0001", MoldName = "Seal Mold A", ProductId = 1, MoldType = "Compression", CavityCount = 4,
            Manufacturer = "Precision", SerialNumber = "SM-1", Location = "MAC-0001", StorageLocation = "Rack A-1",
            CommissionDate = new DateOnly(2022, 1, 10), MaximumShots = 500000, WarningShots = 450000, ReplacementShots = 500000,
            MaintenanceFrequencyShots = 50000, PmCycleStartShots = 100000, CurrentUsageShots = 100000, ResponsibleEmployeeId = 1, Status = "In Production",
            LifeState = "Normal", Remarks = "Main", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1,
            RowVersion = new byte[] { 1 },
        },
        new Mold
        {
            MoldId = 2, MoldCode = "MLD-0002", MoldName = "Gasket Mold", ProductId = 3, MoldType = "Injection", CavityCount = 1,
            MaximumShots = 500000, WarningShots = 450000, ReplacementShots = 500000, CurrentUsageShots = 460000,
            ResponsibleEmployeeId = 3, Status = "Available", LifeState = "Warning",
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Mold
        {
            MoldId = 3, MoldCode = "MLD-0003", MoldName = "Old Mold", ProductId = 2, MoldType = "Compression", CavityCount = 2,
            MaximumShots = 500000, WarningShots = 450000, ReplacementShots = 500000, CurrentUsageShots = 500000,
            Status = "Retired", LifeState = "Replace",
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };

    /// <summary>The persisted computed column life_state, exactly as the DDL defines it.</summary>
    public static string LifeStateOf(Mold m) =>
        m.CurrentUsageShots >= m.ReplacementShots ? MoldLifeState.Replace
        : m.CurrentUsageShots >= m.WarningShots ? MoldLifeState.Warning
        : MoldLifeState.Normal;
}

/// <summary>
/// Behaves like the real MoldRepository where it matters: detached copies on read WITH Product and ResponsibleEmployee
/// loaded, next MLD-NNNN code on add, LifeState recomputed on every write (as SQL Server does), Update/Retire write only
/// the columns the real ones write - and only if the row version still matches.
/// </summary>
internal sealed class InMemoryMoldRepository : IMoldRepository
{
    private readonly List<Mold> _molds;
    private readonly InMemoryProductRepository _products;
    private readonly InMemoryEmployeeRepository _employees;
    private int _nextCode;

    public InMemoryMoldRepository(List<Mold> molds, InMemoryProductRepository products, InMemoryEmployeeRepository employees)
    {
        _molds = molds;
        _products = products;
        _employees = employees;
        _nextCode = molds.Count + 1;
    }

    /// <summary>Runs at the start of Update/Retire - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int RetireCalls { get; private set; }
    public int Count => _molds.Count;

    public Mold Stored(int id) => _molds.Single(m => m.MoldId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static Mold CopyColumns(Mold m) => new()
    {
        MoldId = m.MoldId, MoldCode = m.MoldCode, MoldName = m.MoldName, ProductId = m.ProductId, MoldType = m.MoldType,
        CavityCount = m.CavityCount, Manufacturer = m.Manufacturer, SerialNumber = m.SerialNumber, Location = m.Location,
        StorageLocation = m.StorageLocation, CommissionDate = m.CommissionDate, MaximumShots = m.MaximumShots,
        WarningShots = m.WarningShots, ReplacementShots = m.ReplacementShots, MaintenanceFrequencyShots = m.MaintenanceFrequencyShots,
        PmWarningShots = m.PmWarningShots, PmCycleStartShots = m.PmCycleStartShots,
        CurrentUsageShots = m.CurrentUsageShots, ResponsibleEmployeeId = m.ResponsibleEmployeeId, Status = m.Status,
        LifeState = m.LifeState, Remarks = m.Remarks, CreatedAt = m.CreatedAt, CreatedBy = m.CreatedBy, UpdatedAt = m.UpdatedAt,
        UpdatedBy = m.UpdatedBy, RowVersion = (byte[])m.RowVersion.Clone(),
    };

    private Mold Clone(Mold m)
    {
        var copy = CopyColumns(m);
        copy.Product = _products.Stored(m.ProductId);
        copy.ResponsibleEmployee = m.ResponsibleEmployeeId is { } id ? _employees.Stored(id) : null;
        return copy;
    }

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Mold> Items, int TotalCount)> GetAllAsync(MoldListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Mold> q = _molds;
        if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(m => m.Status == query.Status.Trim());
        if (query.ProductId is { } productId) q = q.Where(m => m.ProductId == productId);
        if (!string.IsNullOrWhiteSpace(query.LifeState)) q = q.Where(m => m.LifeState == query.LifeState.Trim());
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(m => m.MoldCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || m.MoldName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(m => m.MoldName).ThenBy(m => m.MoldId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Mold>, int)>((page, all.Count));
    }

    public Task<Mold?> GetByIdAsync(int moldId, CancellationToken cancellationToken)
    {
        var stored = _molds.FirstOrDefault(m => m.MoldId == moldId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsNonRetiredByNameAsync(string moldName, int? excludeMoldId, CancellationToken cancellationToken) =>
        Task.FromResult(_molds.Any(m =>
            m.Status != MoldStatus.Retired && m.MoldId != excludeMoldId && string.Equals(m.MoldName, moldName, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMoldId, CancellationToken cancellationToken) =>
        Task.FromResult(_molds.Any(m =>
            m.MoldId != excludeMoldId && string.Equals(m.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)));

    public Task<Mold> AddAsync(Mold mold, CancellationToken cancellationToken)
    {
        AddCalls++;
        mold.MoldId = _molds.Max(m => m.MoldId) + 1;
        mold.MoldCode = $"MLD-{_nextCode++:0000}"; // the real repository issues this from the MOLD document sequence
        mold.RowVersion = new byte[] { 1 };
        mold.LifeState = MoldTestData.LifeStateOf(mold);
        _molds.Add(CopyColumns(mold));
        return Task.FromResult(mold);
    }

    public async Task<Mold> UpdateAsync(Mold mold, byte[] originalRowVersion, Func<Mold, CancellationToken, Task>? afterSave, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(mold.MoldId);

        var stored = _molds.FirstOrDefault(m => m.MoldId == mold.MoldId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The mold was modified by another user. Refresh the mold and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never MoldCode or the creation columns.
        stored.MoldName = mold.MoldName;
        stored.ProductId = mold.ProductId;
        stored.MoldType = mold.MoldType;
        stored.CavityCount = mold.CavityCount;
        stored.Manufacturer = mold.Manufacturer;
        stored.SerialNumber = mold.SerialNumber;
        stored.Location = mold.Location;
        stored.StorageLocation = mold.StorageLocation;
        stored.CommissionDate = mold.CommissionDate;
        stored.MaximumShots = mold.MaximumShots;
        stored.WarningShots = mold.WarningShots;
        stored.ReplacementShots = mold.ReplacementShots;
        stored.MaintenanceFrequencyShots = mold.MaintenanceFrequencyShots;
        stored.PmWarningShots = mold.PmWarningShots;
        stored.CurrentUsageShots = mold.CurrentUsageShots;
        stored.ResponsibleEmployeeId = mold.ResponsibleEmployeeId;
        stored.Status = mold.Status;
        stored.Remarks = mold.Remarks;
        stored.UpdatedAt = mold.UpdatedAt;
        stored.UpdatedBy = mold.UpdatedBy;
        stored.LifeState = MoldTestData.LifeStateOf(stored);
        stored.RowVersion = Next(stored.RowVersion);
        mold.RowVersion = stored.RowVersion;
        mold.LifeState = stored.LifeState;

        // The in-transaction evaluation of the saved values (automatic Mold PM).
        if (afterSave is not null)
        {
            await afterSave(mold, cancellationToken);
        }

        return mold;
    }

    public Task<Mold> RetireAsync(Mold mold, CancellationToken cancellationToken)
    {
        RetireCalls++;
        BeforeWrite?.Invoke(mold.MoldId);

        var stored = _molds.FirstOrDefault(m => m.MoldId == mold.MoldId);
        if (stored is null || !stored.RowVersion.SequenceEqual(mold.RowVersion))
        {
            throw new ConflictException("The mold was modified by another user. Refresh the mold and try again.");
        }

        stored.Status = mold.Status;
        stored.UpdatedAt = mold.UpdatedAt;
        stored.UpdatedBy = mold.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        mold.RowVersion = stored.RowVersion;
        return Task.FromResult(mold);
    }
}
