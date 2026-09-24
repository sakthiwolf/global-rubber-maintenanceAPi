using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the employee tests. Departments come from <see cref="DepartmentTestData"/>: 1, 2 active; 3 inactive.</summary>
internal static class EmployeeTestData
{
    // 1 Ravi Kumar (dept 1, active), 2 Karthik Raja (dept 3 = the inactive department, active), 3 Old Hand (inactive).
    public static List<Employee> Employees() => new()
    {
        new Employee
        {
            EmployeeId = 1, EmployeeCode = "EMP-0001", EmployeeName = "Ravi Kumar", Designation = "Maintenance Engineer", DepartmentId = 1,
            Mobile = "9840011122", Email = "ravi@example.com", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Employee
        {
            EmployeeId = 2, EmployeeCode = "EMP-0002", EmployeeName = "Karthik Raja", Designation = "Machine Operator", DepartmentId = 3,
            IsActive = true, CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Employee
        {
            EmployeeId = 3, EmployeeCode = "EMP-0003", EmployeeName = "Old Hand", Designation = "Supervisor", DepartmentId = 2,
            IsActive = false, CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real EmployeeRepository where it matters: reads hand out detached copies WITH the Department loaded,
/// AddAsync issues the next EMP-NNNN code, UpdateAsync/DeactivateAsync write only the columns the real ones write - and
/// only if the row version still matches.
/// </summary>
internal sealed class InMemoryEmployeeRepository : IEmployeeRepository
{
    private readonly List<Employee> _employees;
    private readonly InMemoryDepartmentRepository _departments;
    private int _nextCode;

    public InMemoryEmployeeRepository(List<Employee> employees, InMemoryDepartmentRepository departments)
    {
        _employees = employees;
        _departments = departments;
        _nextCode = employees.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _employees.Count;

    public Employee Stored(int id) => _employees.Single(e => e.EmployeeId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private Employee Clone(Employee e) => new()
    {
        EmployeeId = e.EmployeeId, EmployeeCode = e.EmployeeCode, EmployeeName = e.EmployeeName, Designation = e.Designation,
        DepartmentId = e.DepartmentId, Mobile = e.Mobile, Email = e.Email, IsActive = e.IsActive,
        CreatedAt = e.CreatedAt, CreatedBy = e.CreatedBy, UpdatedAt = e.UpdatedAt, UpdatedBy = e.UpdatedBy,
        RowVersion = (byte[])e.RowVersion.Clone(),
        Department = _departments.Stored(e.DepartmentId),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetAllAsync(EmployeeListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Employee> q = _employees;
        if (query.IsActive is { } active) q = q.Where(e => e.IsActive == active);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(e => e.EmployeeCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || e.EmployeeName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(e => e.EmployeeName).ThenBy(e => e.EmployeeId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Employee>, int)>((page, all.Count));
    }

    public Task<Employee?> GetByIdAsync(int employeeId, CancellationToken cancellationToken)
    {
        var stored = _employees.FirstOrDefault(e => e.EmployeeId == employeeId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<Employee> AddAsync(Employee employee, CancellationToken cancellationToken)
    {
        AddCalls++;
        employee.EmployeeId = _employees.Max(e => e.EmployeeId) + 1;
        employee.EmployeeCode = $"EMP-{_nextCode++:0000}"; // the real repository issues this from the EMPLOYEE document sequence
        employee.RowVersion = new byte[] { 1 };
        _employees.Add(new Employee
        {
            EmployeeId = employee.EmployeeId, EmployeeCode = employee.EmployeeCode, EmployeeName = employee.EmployeeName,
            Designation = employee.Designation, DepartmentId = employee.DepartmentId, Mobile = employee.Mobile, Email = employee.Email,
            IsActive = employee.IsActive, CreatedAt = employee.CreatedAt, CreatedBy = employee.CreatedBy, RowVersion = employee.RowVersion,
        });
        return Task.FromResult(employee);
    }

    public Task<Employee> UpdateAsync(Employee employee, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(employee.EmployeeId);

        var stored = _employees.FirstOrDefault(e => e.EmployeeId == employee.EmployeeId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The employee was modified by another user. Refresh the employee and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never EmployeeCode, IsActive, creation columns.
        stored.EmployeeName = employee.EmployeeName;
        stored.Designation = employee.Designation;
        stored.DepartmentId = employee.DepartmentId;
        stored.Mobile = employee.Mobile;
        stored.Email = employee.Email;
        stored.UpdatedAt = employee.UpdatedAt;
        stored.UpdatedBy = employee.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        employee.RowVersion = stored.RowVersion;
        return Task.FromResult(employee);
    }

    public Task<Employee> DeactivateAsync(Employee employee, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(employee.EmployeeId);

        var stored = _employees.FirstOrDefault(e => e.EmployeeId == employee.EmployeeId);
        if (stored is null || !stored.RowVersion.SequenceEqual(employee.RowVersion))
        {
            throw new ConflictException("The employee was modified by another user. Refresh the employee and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = employee.UpdatedAt;
        stored.UpdatedBy = employee.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        employee.RowVersion = stored.RowVersion;
        return Task.FromResult(employee);
    }
}
