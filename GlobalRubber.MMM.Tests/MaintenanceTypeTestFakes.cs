using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the maintenance type tests (service level and HTTP level).</summary>
internal static class MaintenanceTypeTestData
{
    // 1 Oil & Lubrication (Machine, active), 2 General Inspection (Both, active), 3 Old Type (Mold, inactive).
    public static List<MaintenanceType> MaintenanceTypes() => new()
    {
        new MaintenanceType
        {
            MaintenanceTypeId = 1, MaintenanceTypeCode = "MT-0001", MaintenanceTypeName = "Oil & Lubrication", AppliesTo = "Machine", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new MaintenanceType
        {
            MaintenanceTypeId = 2, MaintenanceTypeCode = "MT-0002", MaintenanceTypeName = "General Inspection", AppliesTo = "Both", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new MaintenanceType
        {
            MaintenanceTypeId = 3, MaintenanceTypeCode = "MT-0003", MaintenanceTypeName = "Old Type", AppliesTo = "Mold", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real MaintenanceTypeRepository where it matters: detached copies on read, next MT-NNNN code on add,
/// Update/Deactivate write only the columns the real ones write - and only if the row version still matches.
/// </summary>
internal sealed class InMemoryMaintenanceTypeRepository : IMaintenanceTypeRepository
{
    private readonly List<MaintenanceType> _types;
    private int _nextCode;

    public InMemoryMaintenanceTypeRepository(List<MaintenanceType> types)
    {
        _types = types;
        _nextCode = types.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _types.Count;

    public MaintenanceType Stored(int id) => _types.Single(t => t.MaintenanceTypeId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static MaintenanceType Clone(MaintenanceType t) => new()
    {
        MaintenanceTypeId = t.MaintenanceTypeId, MaintenanceTypeCode = t.MaintenanceTypeCode, MaintenanceTypeName = t.MaintenanceTypeName,
        AppliesTo = t.AppliesTo, IsActive = t.IsActive, CreatedAt = t.CreatedAt, CreatedBy = t.CreatedBy, UpdatedAt = t.UpdatedAt,
        UpdatedBy = t.UpdatedBy, RowVersion = (byte[])t.RowVersion.Clone(),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<MaintenanceType> Items, int TotalCount)> GetAllAsync(MaintenanceTypeListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<MaintenanceType> q = _types;
        if (query.IsActive is { } active) q = q.Where(t => t.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.AppliesTo)) q = q.Where(t => t.AppliesTo == query.AppliesTo.Trim());
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(t => t.MaintenanceTypeCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || t.MaintenanceTypeName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(t => t.MaintenanceTypeName).ThenBy(t => t.MaintenanceTypeId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<MaintenanceType>, int)>((page, all.Count));
    }

    public Task<MaintenanceType?> GetByIdAsync(int maintenanceTypeId, CancellationToken cancellationToken)
    {
        var stored = _types.FirstOrDefault(t => t.MaintenanceTypeId == maintenanceTypeId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string name, int? excludeMaintenanceTypeId, CancellationToken cancellationToken) =>
        Task.FromResult(_types.Any(t =>
            t.IsActive && t.MaintenanceTypeId != excludeMaintenanceTypeId && string.Equals(t.MaintenanceTypeName, name, StringComparison.OrdinalIgnoreCase)));

    public Task<MaintenanceType> AddAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken)
    {
        AddCalls++;
        maintenanceType.MaintenanceTypeId = _types.Max(t => t.MaintenanceTypeId) + 1;
        maintenanceType.MaintenanceTypeCode = $"MT-{_nextCode++:0000}"; // the real repository issues this from the MAINTENANCE_TYPE sequence
        maintenanceType.RowVersion = new byte[] { 1 };
        _types.Add(Clone(maintenanceType));
        return Task.FromResult(maintenanceType);
    }

    public Task<MaintenanceType> UpdateAsync(MaintenanceType maintenanceType, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(maintenanceType.MaintenanceTypeId);

        var stored = _types.FirstOrDefault(t => t.MaintenanceTypeId == maintenanceType.MaintenanceTypeId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The maintenance type was modified by another user. Refresh the maintenance type and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never the code, IsActive or the creation columns.
        stored.MaintenanceTypeName = maintenanceType.MaintenanceTypeName;
        stored.AppliesTo = maintenanceType.AppliesTo;
        stored.UpdatedAt = maintenanceType.UpdatedAt;
        stored.UpdatedBy = maintenanceType.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        maintenanceType.RowVersion = stored.RowVersion;
        return Task.FromResult(maintenanceType);
    }

    public Task<MaintenanceType> DeactivateAsync(MaintenanceType maintenanceType, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(maintenanceType.MaintenanceTypeId);

        var stored = _types.FirstOrDefault(t => t.MaintenanceTypeId == maintenanceType.MaintenanceTypeId);
        if (stored is null || !stored.RowVersion.SequenceEqual(maintenanceType.RowVersion))
        {
            throw new ConflictException("The maintenance type was modified by another user. Refresh the maintenance type and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = maintenanceType.UpdatedAt;
        stored.UpdatedBy = maintenanceType.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        maintenanceType.RowVersion = stored.RowVersion;
        return Task.FromResult(maintenanceType);
    }
}
