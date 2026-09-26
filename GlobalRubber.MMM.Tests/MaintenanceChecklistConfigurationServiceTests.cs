using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Recurring-PM configuration of the Maintenance Checklist (Function 2, approved 2026-09-25): frequency + machine, the
/// active-checklist rules, the machine lookups. The real MaintenanceChecklistService against in-memory fakes.
/// Checklists: 1 CHK-0001 Machine/Daily/machine 1 (active); 2 CHK-0002 Mold/Weekly (active); 3 CHK-0003 Machine, INACTIVE
/// legacy with no frequency and no machine. Machines: 1 MAC-0001, 2 MAC-0002 active; 3 MAC-0003 inactive.
/// </summary>
public class MaintenanceChecklistConfigurationServiceTests
{
    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Checklists, RecordingAuditLog Audit, List<GlobalRubber.MMM.Domain.Entities.Machine> Machines);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var checklists = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList);
        var audit = new RecordingAuditLog();
        var service = new MaintenanceChecklistService(checklists, new InMemoryMachineRepository(machineList, departments, employees), users,
            new FixedClock(), audit, NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, checklists, audit, machineList);
    }

    private static List<MaintenanceChecklistItemRequest> Items(params string[] labels) =>
        labels.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList();

    private static CreateMaintenanceChecklistRequest New(string appliesTo = "Machine", string? frequency = "Daily", int? machineId = 1, string name = "Injection Machine Daily Inspection") =>
        new() { ChecklistName = name, AppliesTo = appliesTo, Frequency = frequency, MachineId = machineId, StartDate = new DateOnly(2026, 3, 1), Items = Items("Oil level checked", "Hydraulic pressure checked") };

    private static UpdateMaintenanceChecklistRequest Edit(Sut s, int id, string? appliesTo = null, string? frequency = "(keep)", int? machineId = -1, string? name = null) =>
        new()
        {
            ChecklistName = name ?? s.Checklists.Stored(id).ChecklistName,
            AppliesTo = appliesTo ?? s.Checklists.Stored(id).AppliesTo,
            Frequency = frequency == "(keep)" ? s.Checklists.Stored(id).Frequency : frequency,
            MachineId = machineId == -1 ? s.Checklists.Stored(id).MachineId : machineId,
            StartDate = s.Checklists.Stored(id).StartDate ?? new DateOnly(2026, 3, 1),
            Items = Items(s.Checklists.Stored(id).Items.OrderBy(i => i.SortOrder).Select(i => i.ItemLabel).ToArray()),
            RowVersion = Convert.ToBase64String(s.Checklists.Stored(id).RowVersion),
        };

    private static async Task<ValidationException> Invalid(Func<Task> action) => await Assert.ThrowsAsync<ValidationException>(action);

    private static void AssertNothingCreated(Sut s)
    {
        Assert.Equal(0, s.Checklists.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ create

    [Fact] // 1
    public async Task Create_ActiveMachineChecklist_DailyOnAnActiveMachine_Succeeds()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal(("Machine", "Daily", (int?)1, "MAC-0001", (bool?)true), (dto.AppliesTo, dto.Frequency, dto.MachineId, dto.MachineCode, dto.MachineIsActive));
        Assert.Equal("Injection Moulding M/c 1", dto.MachineName);
        Assert.True(dto.IsActive);
        var stored = s.Checklists.Stored(dto.ChecklistId);
        Assert.Equal(("Daily", (int?)1), (stored.Frequency, stored.MachineId));
        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled" }, s.Audit.Entries.Select(e => e.Action)); // Function 3: first occurrence
        var entry = s.Audit.Entries[0];
        Assert.Contains("created daily checklist 'Injection Machine Daily Inspection' (CHK-0004) for machine MAC-0001 starting 2026-03-01 with 2 items.", entry.Description);
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("Yearly")]
    public async Task Create_AcceptsEveryFrequencyTheCheckConstraintAllows(string frequency)
    {
        Assert.Equal(frequency, (await Create().Service.CreateAsync(New(frequency: frequency), 1, null, CancellationToken.None)).Frequency);
    }

    [Fact]
    public async Task Create_FrequencyIsMatchedCaseInsensitively_OntoTheExactValue()
    {
        Assert.Equal("Weekly", (await Create().Service.CreateAsync(New(frequency: "  weekly "), 1, null, CancellationToken.None)).Frequency);
    }

    [Fact] // 2
    public async Task Create_ActiveMachineChecklist_WithoutFrequency_Is400()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(frequency: null), 1, null, CancellationToken.None));

        Assert.Contains("Frequency is required.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact] // 3
    public async Task Create_ActiveMachineChecklist_WithoutMachine_Is400()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(machineId: null), 1, null, CancellationToken.None));

        Assert.Contains("MachineId is required for a Machine checklist.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact] // 4
    public async Task Create_ActiveMachineChecklist_OnAnInactiveMachine_Is400()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(machineId: 3), 1, null, CancellationToken.None));

        Assert.Contains("The selected machine is not active.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact] // 5
    public async Task Create_ActiveMachineChecklist_OnAnUnknownMachine_Is404()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(New(machineId: 999), 1, null, CancellationToken.None));
        AssertNothingCreated(s);
    }

    [Fact] // 6
    public async Task Create_ActiveMoldChecklist_DailyWithoutMachine_Succeeds()
    {
        var dto = await Create().Service.CreateAsync(New(appliesTo: "Mold", machineId: null, name: "Mold Daily Clean"), 1, null, CancellationToken.None);

        Assert.Equal(("Mold", "Daily", (int?)null, (string?)null), (dto.AppliesTo, dto.Frequency, dto.MachineId, dto.MachineCode));
    }

    [Fact] // 7
    public async Task Create_ActiveMoldChecklist_WithAMachine_Is400()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(appliesTo: "Mold", machineId: 1), 1, null, CancellationToken.None));

        Assert.Contains("A Mold checklist cannot have a machine.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Theory] // 8
    [InlineData("Hourly")]
    [InlineData("Fortnightly")]
    [InlineData("Both")]
    public async Task Create_AnInvalidFrequency_Is400(string frequency)
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(frequency: frequency), 1, null, CancellationToken.None));

        Assert.Contains("Frequency must be one of: Daily, Weekly, Monthly, Yearly.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact]
    public async Task Create_ANonPositiveMachineId_Is400()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(New(machineId: 0), 1, null, CancellationToken.None));

        Assert.Contains("MachineId is not valid.", ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact] // 16/17 regression: the configuration errors are reported together with the existing ones, which still work
    public async Task Create_ConfigurationErrors_AreReportedTogetherWithTheExistingFieldErrors()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = new string('n', 151), AppliesTo = "Machine", Frequency = null, MachineId = null, Items = Items("  ", new string('i', 201)),
        }, 1, null, CancellationToken.None));

        Assert.Equal(new[]
        {
            "ChecklistName must be at most 150 characters.", "Frequency is required.", "MachineId is required for a Machine checklist.",
            "StartDate is required for a Machine checklist.", "Checklist item 1 must be at most 200 characters.",
        }, ex.Errors);
        AssertNothingCreated(s);
    }

    [Fact] // 17 regression: active-name uniqueness still wins over a valid configuration
    public async Task Create_DuplicateActiveName_IsStillAConflict()
    {
        var s = Create();

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(New(name: "machine lubrication"), 1, null, CancellationToken.None));
        AssertNothingCreated(s);
    }

    // ================================================================ update: active checklists

    [Fact]
    public async Task Update_ActiveChecklist_CannotDropItsFrequencyOrMachine()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.UpdateAsync(1, Edit(s, 1, frequency: null, machineId: null), 1, null, CancellationToken.None));

        Assert.Contains("Frequency is required.", ex.Errors);
        Assert.Contains("MachineId is required for a Machine checklist.", ex.Errors);
        Assert.Equal(0, s.Checklists.UpdateCalls);
    }

    [Fact]
    public async Task Update_FrequencyAndMachineChanges_AreSaved_AndAuditedOldAndNew()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, frequency: "Monthly", machineId: 2), 1, null, CancellationToken.None);

        Assert.Equal(("Monthly", (int?)2, "MAC-0002"), (dto.Frequency, dto.MachineId, dto.MachineCode));
        Assert.Equal(("Monthly", (int?)2), (s.Checklists.Stored(1).Frequency, s.Checklists.Stored(1).MachineId));
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ChecklistUpdated", entry.Action);
        Assert.Contains("Changed: frequency, machine.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "frequency", OldValue: "Daily", NewValue: "Monthly" });
        Assert.Contains(entry.Details, d => d is { FieldName: "machine", OldValue: "MAC-0001", NewValue: "MAC-0002" });
    }

    [Fact]
    public async Task Update_MachineToMold_MustClearTheMachine()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.UpdateAsync(1, Edit(s, 1, appliesTo: "Mold", machineId: 1), 1, null, CancellationToken.None));
        Assert.Contains("A Mold checklist cannot have a machine.", ex.Errors);

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, appliesTo: "Mold", machineId: null), 1, null, CancellationToken.None);
        Assert.Equal(("Mold", (int?)null), (dto.AppliesTo, dto.MachineId));
    }

    [Fact]
    public async Task Update_MoldToMachine_RequiresAnActiveMachine()
    {
        var s = Create();

        var missing = await Invalid(() => s.Service.UpdateAsync(2, Edit(s, 2, appliesTo: "Machine", machineId: null), 1, null, CancellationToken.None));
        Assert.Contains("MachineId is required for a Machine checklist.", missing.Errors);
        var inactive = await Invalid(() => s.Service.UpdateAsync(2, Edit(s, 2, appliesTo: "Machine", machineId: 3), 1, null, CancellationToken.None));
        Assert.Contains("The selected machine is not active.", inactive.Errors);

        var dto = await s.Service.UpdateAsync(2, Edit(s, 2, appliesTo: "Machine", machineId: 2), 1, null, CancellationToken.None);
        Assert.Equal(("Machine", (int?)2), (dto.AppliesTo, dto.MachineId));
    }

    [Fact]
    public async Task Update_AnUnchangedMachineDeactivatedSince_StaysAssigned_ButCannotBeNewlyChosen()
    {
        var s = Create();
        s.Machines.Single(m => m.MachineId == 1).IsActive = false; // MAC-0001 deactivated after the checklist was set up

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, name: "Machine Lubrication v2"), 1, null, CancellationToken.None);
        Assert.Equal(((int?)1, (bool?)false), (dto.MachineId, dto.MachineIsActive)); // kept, flagged for the UI

        var ex = await Invalid(() => s.Service.UpdateAsync(2, Edit(s, 2, appliesTo: "Machine", machineId: 1), 1, null, CancellationToken.None));
        Assert.Contains("The selected machine is not active.", ex.Errors);
    }

    // ================================================================ update: inactive legacy checklists (9, 10)

    [Fact] // 9 + 10
    public async Task Update_InactiveLegacyChecklist_MayKeepANullFrequencyAndANullMachine()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(3, Edit(s, 3, name: "Old Checklist (legacy)", frequency: null, machineId: null), 1, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        Assert.Equal(((string?)null, (int?)null), (dto.Frequency, dto.MachineId));
        Assert.Equal("Old Checklist (legacy)", s.Checklists.Stored(3).ChecklistName);
        Assert.False(s.Checklists.Stored(3).IsActive); // never reactivated by an edit
    }

    [Fact]
    public async Task Update_InactiveLegacyChecklist_StillCannotHoldAnInvalidConfiguration()
    {
        var s = Create();

        var frequency = await Invalid(() => s.Service.UpdateAsync(3, Edit(s, 3, frequency: "Hourly"), 1, null, CancellationToken.None));
        Assert.Contains("Frequency must be one of: Daily, Weekly, Monthly, Yearly.", frequency.Errors);          // CK_..._frequency
        var mold = await Invalid(() => s.Service.UpdateAsync(3, Edit(s, 3, appliesTo: "Mold", machineId: 1), 1, null, CancellationToken.None));
        Assert.Contains("A Mold checklist cannot have a machine.", mold.Errors);                                // CK_..._machine
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(3, Edit(s, 3, machineId: 999), 1, null, CancellationToken.None));
        Assert.Equal(0, s.Checklists.UpdateCalls);
    }

    // ================================================================ lookups (11, 12, 13) and list filters

    [Fact] // 11 + 12
    public async Task Lookups_ReturnActiveMachinesOnly_ByCode()
    {
        var lookups = await Create().Service.GetLookupsAsync(CancellationToken.None);

        Assert.Equal(new[] { (1, "MAC-0001"), (2, "MAC-0002") }, lookups.Machines.Select(m => (m.MachineId, m.MachineCode)));
        Assert.DoesNotContain(lookups.Machines, m => m.MachineId == 3);
    }

    [Fact] // 13
    public async Task AMachineMissingFromTheLookups_CannotBeSelectedThroughValidation()
    {
        var s = Create();
        var offered = (await s.Service.GetLookupsAsync(CancellationToken.None)).Machines.Select(m => m.MachineId).ToHashSet();

        Assert.DoesNotContain(3, offered);
        await Invalid(() => s.Service.CreateAsync(New(machineId: 3), 1, null, CancellationToken.None));                 // inactive
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(New(machineId: 42), 1, null, CancellationToken.None)); // unknown
        AssertNothingCreated(s);
    }

    [Fact]
    public async Task List_FiltersByMachineAndFrequency_AndReturnsTheConfiguration()
    {
        var s = Create();
        async Task<List<string>> Codes(MaintenanceChecklistListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.ChecklistCode).ToList();

        Assert.Equal(new[] { "CHK-0001" }, await Codes(new MaintenanceChecklistListQuery { MachineId = 1 }));
        Assert.Equal(new[] { "CHK-0002" }, await Codes(new MaintenanceChecklistListQuery { Frequency = "Weekly" }));
        Assert.Empty(await Codes(new MaintenanceChecklistListQuery { Frequency = "Hourly" }));

        var legacy = await s.Service.GetByIdAsync(3, CancellationToken.None);
        Assert.Equal(((string?)null, (int?)null, false), (legacy.Frequency, legacy.MachineId, legacy.IsActive));
    }

    [Fact]
    public void AuditActions_StillFitVarchar30()
    {
        Assert.All(new[] { "ChecklistCreated", "ChecklistUpdated", "ChecklistDeactivated" }, a => Assert.True(a.Length <= 30));
    }
}
