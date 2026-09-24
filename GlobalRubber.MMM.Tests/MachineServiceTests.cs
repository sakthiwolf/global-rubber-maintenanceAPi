using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real MachineService against in-memory fakes. Acting user 1 "Sakthi". See <see cref="MachineTestData"/> for the
/// machines, departments (3 inactive) and employees (3 inactive).
/// </summary>
public class MachineServiceTests
{
    private sealed record Sut(MachineService Service, InMemoryMachineRepository Machines, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var audit = new RecordingAuditLog();
        var service = new MachineService(machines, departments, users, new FixedClock(), auditOverride ?? audit, NullLogger<MachineService>.Instance);
        return new Sut(service, machines, audit, users);
    }

    private static CreateMachineRequest NewMachine(
        string name = "Banbury Mixer", int departmentId = 2, string? serial = "BR270-5521",
        int? frequency = 45, string? criticality = "High") => new()
    {
        MachineName = name, MachineType = "Mixer", DepartmentId = departmentId, Location = "Shop Floor 2 - Bay 2",
        Manufacturer = "Farrel", Model = "BR-270", SerialNumber = serial, Capacity = "270 Litre",
        InstallationDate = new DateOnly(2017, 5, 10), MaintenanceFrequencyDays = frequency,
        Criticality = criticality, Remarks = "New",
    };

    private static string Rv(InMemoryMachineRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request copying the stored machine ("no change"), with the given overrides applied.
    private static UpdateMachineRequest Edit(InMemoryMachineRepository repo, int id, Func<UpdateMachineRequest, UpdateMachineRequest>? change = null)
    {
        var s = repo.Stored(id);
        var request = new UpdateMachineRequest
        {
            MachineName = s.MachineName, MachineType = s.MachineType, DepartmentId = s.DepartmentId, Location = s.Location,
            Manufacturer = s.Manufacturer, Model = s.Model, SerialNumber = s.SerialNumber, Capacity = s.Capacity,
            InstallationDate = s.InstallationDate, MaintenanceFrequencyDays = s.MaintenanceFrequencyDays,
            Criticality = s.Criticality, Remarks = s.Remarks,
            RowVersion = Rv(repo, id),
        };
        return change is null ? request : change(request);
    }

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName_WithJoinedNames()
    {
        var result = await Create().Service.GetAllAsync(new MachineListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Injection Moulding M/c 1", "Mixing Mill" }, result.Items.Select(i => i.MachineName));
        Assert.Equal("Injection Moulding", result.Items[0].DepartmentName);
        Assert.False(result.Items[1].DepartmentIsActive);
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_Department_OperationalStatus_AndSearch()
    {
        var s = Create();
        async Task<List<string>> Names(MachineListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.MachineName).ToList();

        Assert.Equal(new[] { "Injection Moulding M/c 1", "Mixing Mill" }, await Names(new MachineListQuery { IsActive = true }));
        Assert.Equal(new[] { "Old Machine" }, await Names(new MachineListQuery { IsActive = false }));
        Assert.Equal(new[] { "Mixing Mill" }, await Names(new MachineListQuery { DepartmentId = 3 }));
        Assert.Equal(new[] { "Mixing Mill" }, await Names(new MachineListQuery { OperationalStatus = "Breakdown" }));
        Assert.Equal(new[] { "Injection Moulding M/c 1" }, await Names(new MachineListQuery { Search = "moulding" }));
        Assert.Equal(new[] { "Old Machine" }, await Names(new MachineListQuery { Search = "mac-0003" }));
        Assert.Empty(await Names(new MachineListQuery { OperationalStatus = "Flying" }));
    }

    [Fact]
    public async Task GetById_ReturnsEveryField_IncludingSystemManagedOnes()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("MAC-0001", dto.MachineCode);
        Assert.Equal("Injection Moulding", dto.MachineType);
        Assert.Equal("Shop Floor 1 - Bay 1", dto.Location);
        Assert.Equal("LTD250-1145", dto.SerialNumber);
        Assert.Equal("250 Ton", dto.Capacity);
        Assert.Equal(new DateOnly(2019, 3, 14), dto.InstallationDate);
        Assert.Equal(30, dto.MaintenanceFrequencyDays);
        Assert.Equal("High", dto.Criticality);
        Assert.Equal("Running", dto.OperationalStatus);
        Assert.Equal(new DateOnly(2026, 2, 1), dto.LastMaintenanceDate);
        Assert.Equal(new DateOnly(2026, 3, 3), dto.NextMaintenanceDate);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task GetById_ThrowsNotFound_ForAnUnknownMachine()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_IsRunningAndActive_SetsCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewMachine(name: "  Banbury Mixer ", criticality: "high"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("MAC-0004", dto.MachineCode);
        Assert.Equal("Banbury Mixer", dto.MachineName);
        Assert.Equal("Running", dto.OperationalStatus); // BR-05
        Assert.Equal("High", dto.Criticality);          // normalized onto the CK value
        Assert.True(dto.IsActive);
        Assert.Equal("Mixing", dto.DepartmentName);
        Assert.Null(dto.LastMaintenanceDate);
        Assert.Null(dto.NextMaintenanceDate);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MachineCreated", entry.Action);
        Assert.Equal("Machine", entry.Module);
        Assert.Equal("Machine", entry.EntityName);
        Assert.Equal(dto.MachineId, entry.EntityId);
        Assert.Equal("MAC-0004", entry.RecordRef);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Create_WithoutSerialNumber_IsAllowed()
    {
        var dto = await Create().Service.CreateAsync(NewMachine(serial: "  "), 1, null, CancellationToken.None);

        Assert.Null(dto.SerialNumber);
    }

    // ---- Responsible Engineer temporarily disabled (database field and relationship retained)

    [Fact]
    public async Task Create_NeverSetsAResponsibleEngineer_AndNeverLooksOneUp()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewMachine(), 1, null, CancellationToken.None);

        Assert.Null(s.Machines.Stored(dto.MachineId).ResponsibleEngineerId);
        Assert.DoesNotContain("engineer", System.Text.Json.JsonSerializer.Serialize(dto), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_PreservesAnExistingResponsibleEngineer_EvenAnInactiveOne_AndDoesNotAuditIt()
    {
        var s = Create();

        // Machine 1 has engineer 1 (active); machine 2 has engineer 3 (inactive) - neither may be cleared or validated.
        var one = await s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => Copy(r, name: "IM M/c 1 renamed")), 1, null, CancellationToken.None);
        var two = await s.Service.UpdateAsync(2, Edit(s.Machines, 2, r => Copy(r, name: "Mixing Mill renamed")), 1, null, CancellationToken.None);

        Assert.Equal("IM M/c 1 renamed", one.MachineName);
        Assert.Equal("Mixing Mill renamed", two.MachineName);
        Assert.Equal(1, s.Machines.Stored(1).ResponsibleEngineerId);
        Assert.Equal(3, s.Machines.Stored(2).ResponsibleEngineerId);
        Assert.All(s.Audit.Entries, e =>
        {
            Assert.DoesNotContain("engineer", e.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(e.Details, d => d.FieldName.Contains("engineer", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public async Task Create_RequiresTheBR04FieldsAndCriticality_WithAllErrors_WritingNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(
            new CreateMachineRequest { MachineName = " ", MachineType = "", DepartmentId = 0, Location = " " }, 1, null, CancellationToken.None));

        Assert.Contains("MachineName is required.", ex.Errors);
        Assert.Contains("MachineType is required.", ex.Errors);
        Assert.Contains("DepartmentId is required.", ex.Errors);
        Assert.Contains("Location is required.", ex.Errors);
        Assert.Contains("MaintenanceFrequencyDays is required.", ex.Errors);
        Assert.Contains("Criticality is required.", ex.Errors);
        Assert.Equal(0, s.Machines.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Create_RejectsANonPositiveFrequency(int frequency)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create().Service.CreateAsync(NewMachine(frequency: frequency), 1, null, CancellationToken.None));

        Assert.Contains("MaintenanceFrequencyDays must be greater than 0.", ex.Errors);
    }

    [Fact]
    public async Task Create_RejectsACriticalityOutsideTheCheckList()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create().Service.CreateAsync(NewMachine(criticality: "Urgent"), 1, null, CancellationToken.None));

        Assert.Contains("Criticality must be one of: Low, Medium, High.", ex.Errors);
    }

    [Fact]
    public async Task Create_EnforcesEveryColumnLength()
    {
        var s = Create();
        var request = new CreateMachineRequest
        {
            MachineName = new string('n', 151), MachineType = new string('t', 101), DepartmentId = 1, Location = new string('l', 151),
            Manufacturer = new string('m', 101), Model = new string('o', 101), SerialNumber = new string('s', 101),
            Capacity = new string('c', 51), MaintenanceFrequencyDays = 30, Criticality = "Low", Remarks = new string('r', 501),
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(request, 1, null, CancellationToken.None));

        foreach (var expected in new[]
        {
            "MachineName must be at most 150 characters.", "MachineType must be at most 100 characters.", "Location must be at most 150 characters.",
            "Manufacturer must be at most 100 characters.", "Model must be at most 100 characters.", "SerialNumber must be at most 100 characters.",
            "Capacity must be at most 50 characters.", "Remarks must be at most 500 characters.",
        })
        {
            Assert.Contains(expected, ex.Errors);
        }
    }

    [Fact]
    public async Task Create_UnknownDepartment_IsNotFound_AnInactiveOne_IsAValidationError()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewMachine(departmentId: 999), 1, null, CancellationToken.None));

        var inactiveDept = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewMachine(departmentId: 3), 1, null, CancellationToken.None));
        Assert.Contains("The selected department is not active.", inactiveDept.Errors);

        Assert.Equal(0, s.Machines.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RefusesADuplicateActiveName_AndATakenSerialNumber()
    {
        var s = Create();

        var name = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewMachine(name: "injection moulding m/c 1"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", name.Message);
        var serial = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewMachine(serial: "ltd250-1145"), 1, null, CancellationToken.None));
        Assert.Contains("serial number", serial.Message);

        Assert.Equal(0, s.Machines.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveMachine()
    {
        var dto = await Create().Service.CreateAsync(NewMachine(name: "Old Machine"), 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesTheEditableFields_NeverTheSystemManagedOnes()
    {
        var s = Create();
        var before = s.Machines.Stored(1);
        var snapshot = (before.MachineCode, before.IsActive, before.OperationalStatus, before.LastMaintenanceDate, before.NextMaintenanceDate, before.CreatedBy, before.CreatedAt);
        var oldVersion = Rv(s.Machines, 1);

        var dto = await s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => new UpdateMachineRequest
        {
            MachineName = "IM M/c 1", MachineType = "Injection", DepartmentId = 2, Location = "Bay 9", Manufacturer = null, Model = "D300",
            SerialNumber = "NEW-1", Capacity = null, InstallationDate = null, MaintenanceFrequencyDays = 15,
            Criticality = "Low", Remarks = null, RowVersion = r.RowVersion,
        }), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("IM M/c 1", dto.MachineName);
        Assert.Equal("Mixing", dto.DepartmentName);
        Assert.Equal(15, dto.MaintenanceFrequencyDays);
        Assert.Equal("Low", dto.Criticality);
        Assert.Null(dto.InstallationDate);
        var after = s.Machines.Stored(1);
        Assert.Equal(snapshot, (after.MachineCode, after.IsActive, after.OperationalStatus, after.LastMaintenanceDate, after.NextMaintenanceDate, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);
    }

    [Fact]
    public async Task Update_AuditsFieldLevelChanges_WithOldAndNewValues_AndCodesForTheDepartment()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => new UpdateMachineRequest
        {
            MachineName = r.MachineName, MachineType = r.MachineType, DepartmentId = 2, Location = r.Location, Manufacturer = r.Manufacturer,
            Model = r.Model, SerialNumber = r.SerialNumber, Capacity = r.Capacity, InstallationDate = new DateOnly(2020, 1, 2),
            MaintenanceFrequencyDays = 20, Criticality = r.Criticality, Remarks = r.Remarks, RowVersion = r.RowVersion,
        }), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MachineUpdated", entry.Action);
        Assert.Equal("MAC-0001", entry.RecordRef);
        Assert.Contains("Changed: department_code, installation_date, maintenance_frequency_days.", entry.Description);
        Assert.Equal(3, entry.Details.Count);
        Assert.Contains(entry.Details, d => d is { FieldName: "department_code", OldValue: "DEP-0001", NewValue: "DEP-0002" });
        Assert.Contains(entry.Details, d => d is { FieldName: "installation_date", OldValue: "2019-03-14", NewValue: "2020-01-02" });
        Assert.Contains(entry.Details, d => d is { FieldName: "maintenance_frequency_days", OldValue: "30", NewValue: "20" });
    }

    [Fact]
    public async Task Update_KeepingAnInactiveDepartment_IsAllowed_ButChoosingOneIsNot()
    {
        var s = Create();

        // Machine 2's department (3) has been deactivated: an unrelated edit still saves.
        var kept = await s.Service.UpdateAsync(2, Edit(s.Machines, 2, r => new UpdateMachineRequest
        {
            MachineName = r.MachineName, MachineType = r.MachineType, DepartmentId = r.DepartmentId, Location = "Shop Floor 2 - Bay 3",
            MaintenanceFrequencyDays = r.MaintenanceFrequencyDays, Criticality = r.Criticality, RowVersion = r.RowVersion,
        }), 1, null, CancellationToken.None);
        Assert.Equal(3, kept.DepartmentId);
        Assert.False(kept.DepartmentIsActive);

        // Machine 1 moving to the inactive department is refused.
        var dept = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => Copy(r, departmentId: 3)), 1, null, CancellationToken.None));
        Assert.Contains("The selected department is not active.", dept.Errors);
        Assert.Equal(1, s.Machines.Stored(1).DepartmentId);
    }

    private static UpdateMachineRequest Copy(UpdateMachineRequest r, int? departmentId = null, string? name = null, string? serial = "keep") => new()
    {
        MachineName = name ?? r.MachineName, MachineType = r.MachineType, DepartmentId = departmentId ?? r.DepartmentId, Location = r.Location,
        Manufacturer = r.Manufacturer, Model = r.Model, SerialNumber = serial == "keep" ? r.SerialNumber : serial, Capacity = r.Capacity,
        InstallationDate = r.InstallationDate, MaintenanceFrequencyDays = r.MaintenanceFrequencyDays,
        Criticality = r.Criticality, Remarks = r.Remarks, RowVersion = r.RowVersion,
    };

    [Fact]
    public async Task Update_UnknownMachine_OrUnknownNewDepartment_AreNotFound()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s.Machines, 1), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => Copy(r, departmentId: 999)), 1, null, CancellationToken.None));

        Assert.Equal(0, s.Machines.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_KeepsItsOwnNameAndSerial_ButRefusesAnotherMachines()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => Copy(r, name: "INJECTION MOULDING M/C 1")), 1, null, CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s.Machines, 2, r => Copy(r, name: "injection moulding m/c 1")), 1, null, CancellationToken.None));
        var serial = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s.Machines, 2, r => Copy(r, serial: "LTD250-1145")), 1, null, CancellationToken.None));
        Assert.Contains("serial number", serial.Message);
        Assert.Single(s.Audit.Entries); // only the first, successful update
    }

    [Fact]
    public async Task Update_ValidatesFields_AndTheRowVersion()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1,
            new UpdateMachineRequest { MachineName = "", MachineType = "T", DepartmentId = 1, Location = "L", MaintenanceFrequencyDays = 0, Criticality = "x", RowVersion = "" },
            1, null, CancellationToken.None));
        Assert.Contains("MachineName is required.", ex.Errors);
        Assert.Contains("MaintenanceFrequencyDays must be greater than 0.", ex.Errors);
        Assert.Contains("Criticality must be one of: Low, Medium, High.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Machines, 1, r => new UpdateMachineRequest
        {
            MachineName = r.MachineName, MachineType = r.MachineType, DepartmentId = r.DepartmentId, Location = r.Location,
            MaintenanceFrequencyDays = r.MaintenanceFrequencyDays, Criticality = r.Criticality, RowVersion = "not base64 !!",
        }), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Machines.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var stale = Edit(s.Machines, 1, r => Copy(r, name: "Stale edit"));
        s.Machines.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Injection Moulding M/c 1", s.Machines.Stored(1).MachineName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRowAndOperationalStatus_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.DeactivateAsync(2, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Machines.Stored(2);
        Assert.False(after.IsActive);
        Assert.Equal("Breakdown", after.OperationalStatus); // untouched - no rule links the two
        Assert.Equal("Mixing Mill", after.MachineName);
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(3, s.Machines.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MachineDeactivated", entry.Action);
        Assert.Equal("MAC-0002", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Deactivate_ThrowsNotFound_ForUnknown_AndConflict_ForAlreadyInactive_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Contains("already inactive", ex.Message);
        Assert.Equal(0, s.Machines.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Machines.BeforeWrite = s.Machines.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Machines.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewMachine(), 1, null, CancellationToken.None);

        Assert.Equal("MAC-0004", dto.MachineCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewMachine(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.MachineId, Edit(s.Machines, created.MachineId, r => Copy(r, name: "Banbury Mixer 2")), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.MachineId, 1, null, CancellationToken.None);

        Assert.Equal("Banbury Mixer 2", updated.MachineName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Machines.Stored(created.MachineId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
