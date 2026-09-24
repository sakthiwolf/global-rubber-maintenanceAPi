using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the department tests (service level and HTTP level).</summary>
internal static class DepartmentTestData
{
    // 1 Injection Moulding (active), 2 Mixing (active), 3 Old Dept (inactive).
    public static List<Department> Departments() => new()
    {
        new Department
        {
            DepartmentId = 1, DepartmentCode = "DEP-0001", DepartmentName = "Injection Moulding", Remarks = "Shop floor 1", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Department
        {
            DepartmentId = 2, DepartmentCode = "DEP-0002", DepartmentName = "Mixing", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Department
        {
            DepartmentId = 3, DepartmentCode = "DEP-0003", DepartmentName = "Old Dept", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real DepartmentRepository where it matters: reads hand out detached copies, AddAsync issues
/// the next DEP-NNNN code, UpdateAsync/DeactivateAsync write only the columns the real ones write - and only if the
/// row version still matches.
/// </summary>
internal sealed class InMemoryDepartmentRepository : IDepartmentRepository
{
    private readonly List<Department> _departments;
    private int _nextCode;

    public InMemoryDepartmentRepository(List<Department> departments)
    {
        _departments = departments;
        _nextCode = departments.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _departments.Count;

    public Department Stored(int id) => _departments.Single(d => d.DepartmentId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static Department Clone(Department d) => new()
    {
        DepartmentId = d.DepartmentId, DepartmentCode = d.DepartmentCode, DepartmentName = d.DepartmentName, Remarks = d.Remarks,
        IsActive = d.IsActive, CreatedAt = d.CreatedAt, CreatedBy = d.CreatedBy, UpdatedAt = d.UpdatedAt, UpdatedBy = d.UpdatedBy,
        RowVersion = (byte[])d.RowVersion.Clone(),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Department> Items, int TotalCount)> GetAllAsync(DepartmentListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Department> q = _departments;
        if (query.IsActive is { } active) q = q.Where(d => d.IsActive == active);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(d => d.DepartmentCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || d.DepartmentName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(d => d.DepartmentName).ThenBy(d => d.DepartmentId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Department>, int)>((page, all.Count));
    }

    public Task<Department?> GetByIdAsync(int departmentId, CancellationToken cancellationToken)
    {
        var stored = _departments.FirstOrDefault(d => d.DepartmentId == departmentId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string departmentName, int? excludeDepartmentId, CancellationToken cancellationToken) =>
        Task.FromResult(_departments.Any(d =>
            d.IsActive && d.DepartmentId != excludeDepartmentId
            && string.Equals(d.DepartmentName, departmentName, StringComparison.OrdinalIgnoreCase)));

    public Task<Department> AddAsync(Department department, CancellationToken cancellationToken)
    {
        AddCalls++;
        department.DepartmentId = _departments.Max(d => d.DepartmentId) + 1;
        department.DepartmentCode = $"DEP-{_nextCode++:0000}"; // the real repository issues this from the DEPARTMENT document sequence
        department.RowVersion = new byte[] { 1 };
        _departments.Add(Clone(department));
        return Task.FromResult(department);
    }

    public Task<Department> UpdateAsync(Department department, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(department.DepartmentId);

        var stored = _departments.FirstOrDefault(d => d.DepartmentId == department.DepartmentId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The department was modified by another user. Refresh the department and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never DepartmentCode, IsActive, creation columns.
        stored.DepartmentName = department.DepartmentName;
        stored.Remarks = department.Remarks;
        stored.UpdatedAt = department.UpdatedAt;
        stored.UpdatedBy = department.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        department.RowVersion = stored.RowVersion;
        return Task.FromResult(department);
    }

    public Task<Department> DeactivateAsync(Department department, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(department.DepartmentId);

        var stored = _departments.FirstOrDefault(d => d.DepartmentId == department.DepartmentId);
        if (stored is null || !stored.RowVersion.SequenceEqual(department.RowVersion))
        {
            throw new ConflictException("The department was modified by another user. Refresh the department and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = department.UpdatedAt;
        stored.UpdatedBy = department.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        department.RowVersion = stored.RowVersion;
        return Task.FromResult(department);
    }
}
