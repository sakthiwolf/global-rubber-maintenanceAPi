using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Shared fakes for the machine tests. Departments (<see cref="DepartmentTestData"/>): 1, 2 active, 3 inactive.
/// Employees (<see cref="EmployeeTestData"/>): 1 Ravi Kumar, 2 Karthik Raja active, 3 Old Hand inactive.
/// </summary>
internal static class MachineTestData
{
    // 1 Injection Moulding M/c 1 (dept 1, engineer 1, serial, Running), 2 Mixing Mill (dept 3 = inactive, engineer 3 =
    // inactive, Breakdown), 3 Old Machine (inactive, dept 2, no engineer).
    public static List<Machine> Machines() => new()
    {
        new Machine
        {
            MachineId = 1, MachineCode = "MAC-0001", MachineName = "Injection Moulding M/c 1", MachineType = "Injection Moulding",
            DepartmentId = 1, Location = "Shop Floor 1 - Bay 1", Manufacturer = "L&T Demag", Model = "D250", SerialNumber = "LTD250-1145",
            Capacity = "250 Ton", InstallationDate = new DateOnly(2019, 3, 14), MaintenanceFrequencyDays = 30, ResponsibleEngineerId = 1,
            Criticality = "High", OperationalStatus = "Running", LastMaintenanceDate = new DateOnly(2026, 2, 1),
            NextMaintenanceDate = new DateOnly(2026, 3, 3), Remarks = "Main line", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Machine
        {
            MachineId = 2, MachineCode = "MAC-0002", MachineName = "Mixing Mill", MachineType = "Mixing Mill", DepartmentId = 3,
            Location = "Shop Floor 2", MaintenanceFrequencyDays = 45, ResponsibleEngineerId = 3, Criticality = "Medium",
            OperationalStatus = "Breakdown", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Machine
        {
            MachineId = 3, MachineCode = "MAC-0003", MachineName = "Old Machine", MachineType = "Cutting", DepartmentId = 2,
            Location = "Store", MaintenanceFrequencyDays = 60, Criticality = "Low", OperationalStatus = "Idle", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real MachineRepository where it matters: detached copies on read WITH Department and
/// ResponsibleEngineer loaded, next MAC-NNNN code on add, Update/Deactivate write only the columns the real ones write -
/// and only if the row version still matches.
/// </summary>
internal sealed class InMemoryMachineRepository : IMachineRepository
{
    private readonly List<Machine> _machines;
    private readonly InMemoryDepartmentRepository _departments;
    private readonly InMemoryEmployeeRepository _employees;
    private int _nextCode;

    public InMemoryMachineRepository(List<Machine> machines, InMemoryDepartmentRepository departments, InMemoryEmployeeRepository employees)
    {
        _machines = machines;
        _departments = departments;
        _employees = employees;
        _nextCode = machines.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _machines.Count;

    public Machine Stored(int id) => _machines.Single(m => m.MachineId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static Machine CopyColumns(Machine m) => new()
    {
        MachineId = m.MachineId, MachineCode = m.MachineCode, MachineName = m.MachineName, MachineType = m.MachineType,
        DepartmentId = m.DepartmentId, Location = m.Location, Manufacturer = m.Manufacturer, Model = m.Model,
        SerialNumber = m.SerialNumber, Capacity = m.Capacity, InstallationDate = m.InstallationDate,
        MaintenanceFrequencyDays = m.MaintenanceFrequencyDays, ResponsibleEngineerId = m.ResponsibleEngineerId,
        Criticality = m.Criticality, OperationalStatus = m.OperationalStatus, LastMaintenanceDate = m.LastMaintenanceDate,
        NextMaintenanceDate = m.NextMaintenanceDate, Remarks = m.Remarks, IsActive = m.IsActive, CreatedAt = m.CreatedAt,
        CreatedBy = m.CreatedBy, UpdatedAt = m.UpdatedAt, UpdatedBy = m.UpdatedBy, RowVersion = (byte[])m.RowVersion.Clone(),
    };

    private Machine Clone(Machine m)
    {
        var copy = CopyColumns(m);
        copy.Department = _departments.Stored(m.DepartmentId);
        copy.ResponsibleEngineer = m.ResponsibleEngineerId is { } id ? _employees.Stored(id) : null;
        return copy;
    }

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Machine> Items, int TotalCount)> GetAllAsync(MachineListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Machine> q = _machines;
        if (query.IsActive is { } active) q = q.Where(m => m.IsActive == active);
        if (query.DepartmentId is { } departmentId) q = q.Where(m => m.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(query.OperationalStatus)) q = q.Where(m => m.OperationalStatus == query.OperationalStatus.Trim());
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(m => m.MachineCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || m.MachineName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(m => m.MachineName).ThenBy(m => m.MachineId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Machine>, int)>((page, all.Count));
    }

    public Task<Machine?> GetByIdAsync(int machineId, CancellationToken cancellationToken)
    {
        var stored = _machines.FirstOrDefault(m => m.MachineId == machineId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string machineName, int? excludeMachineId, CancellationToken cancellationToken) =>
        Task.FromResult(_machines.Any(m =>
            m.IsActive && m.MachineId != excludeMachineId && string.Equals(m.MachineName, machineName, StringComparison.OrdinalIgnoreCase)));

    public Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMachineId, CancellationToken cancellationToken) =>
        Task.FromResult(_machines.Any(m =>
            m.MachineId != excludeMachineId && string.Equals(m.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)));

    public Task<Machine> AddAsync(Machine machine, CancellationToken cancellationToken)
    {
        AddCalls++;
        machine.MachineId = _machines.Max(m => m.MachineId) + 1;
        machine.MachineCode = $"MAC-{_nextCode++:0000}"; // the real repository issues this from the MACHINE document sequence
        machine.RowVersion = new byte[] { 1 };
        _machines.Add(CopyColumns(machine));
        return Task.FromResult(machine);
    }

    public Task<Machine> UpdateAsync(Machine machine, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(machine.MachineId);

        var stored = _machines.FirstOrDefault(m => m.MachineId == machine.MachineId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The machine was modified by another user. Refresh the machine and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never MachineCode, IsActive, OperationalStatus, the
        // last/next maintenance dates, ResponsibleEngineerId or the creation columns.
        stored.MachineName = machine.MachineName;
        stored.MachineType = machine.MachineType;
        stored.DepartmentId = machine.DepartmentId;
        stored.Location = machine.Location;
        stored.Manufacturer = machine.Manufacturer;
        stored.Model = machine.Model;
        stored.SerialNumber = machine.SerialNumber;
        stored.Capacity = machine.Capacity;
        stored.InstallationDate = machine.InstallationDate;
        stored.MaintenanceFrequencyDays = machine.MaintenanceFrequencyDays;
        // ResponsibleEngineerId is never written (Responsible Engineer temporarily disabled) - same as the real repository.
        stored.Criticality = machine.Criticality;
        stored.Remarks = machine.Remarks;
        stored.UpdatedAt = machine.UpdatedAt;
        stored.UpdatedBy = machine.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        machine.RowVersion = stored.RowVersion;
        return Task.FromResult(machine);
    }

    public Task<Machine> DeactivateAsync(Machine machine, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(machine.MachineId);

        var stored = _machines.FirstOrDefault(m => m.MachineId == machine.MachineId);
        if (stored is null || !stored.RowVersion.SequenceEqual(machine.RowVersion))
        {
            throw new ConflictException("The machine was modified by another user. Refresh the machine and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = machine.UpdatedAt;
        stored.UpdatedBy = machine.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        machine.RowVersion = stored.RowVersion;
        return Task.FromResult(machine);
    }
}
