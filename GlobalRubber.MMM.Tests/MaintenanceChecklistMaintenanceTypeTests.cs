using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Migration 017: a Preventive Maintenance Plan (Machine checklist) may carry a maintenance type, which every PM occurrence
/// of the plan carries too. Types: 1 Oil &amp; Lubrication (Machine, active), 2 General Inspection (Both, active), 3 Old Type
/// (Mold, inactive), 4 Retired Machine Type (Machine, inactive). The plan under test is created on machine 2 with no type,
/// which creates its open occurrence MPM-0001; two completed occurrences of it are then seeded - MPM-0002 untyped and
/// MPM-0003 already typed 2 (history that must never be rewritten).
/// </summary>
public class MaintenanceChecklistMaintenanceTypeTests
{
    private static readonly DateOnly Today = new FixedClock().Today;

    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Repo, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var types = MaintenanceTypeTestData.MaintenanceTypes();
        types.Add(new MaintenanceType { MaintenanceTypeId = 4, MaintenanceTypeCode = "MT-0004", MaintenanceTypeName = "Retired Machine Type", AppliesTo = "Machine", IsActive = false, RowVersion = new byte[] { 1 } });
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList);
        var audit = new RecordingAuditLog();
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees), new InMemoryMaintenanceTypeRepository(types), users,
            new FixedClock(), audit, NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, repo, audit);
    }

    private static CreateMaintenanceChecklistRequest New(int? maintenanceTypeId = null, string appliesTo = "Machine") =>
        new()
        {
            ChecklistName = "Press Daily Inspection", AppliesTo = appliesTo, Frequency = "Daily",
            MachineId = appliesTo == "Machine" ? 2 : null, StartDate = Today, MaintenanceTypeId = maintenanceTypeId,
            Items = new[] { new MaintenanceChecklistItemRequest { ItemLabel = "Oil level checked" } },
        };

    private static UpdateMaintenanceChecklistRequest Edit(Sut s, int checklistId, int? maintenanceTypeId, string? name = null)
    {
        var stored = s.Repo.Stored(checklistId);
        return new UpdateMaintenanceChecklistRequest
        {
            ChecklistName = name ?? stored.ChecklistName, AppliesTo = stored.AppliesTo, Frequency = stored.Frequency, MachineId = stored.MachineId,
            StartDate = stored.StartDate, MaintenanceTypeId = maintenanceTypeId,
            Items = stored.Items.OrderBy(i => i.SortOrder).Select(i => new MaintenanceChecklistItemRequest { ItemLabel = i.ItemLabel }).ToList(),
            RowVersion = Convert.ToBase64String(stored.RowVersion),
        };
    }

    // A plan without a type, its open MPM-0001, and two completed occurrences: MPM-0002 untyped, MPM-0003 typed 2.
    private static async Task<(Sut S, int Id)> PlanWithHistory()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);
        foreach (var (no, type) in new[] { ("MPM-0002", (int?)null), ("MPM-0003", (int?)2) })
        {
            s.Repo.Pms.Add(new MachinePm
            {
                MachinePmId = s.Repo.Pms.Max(p => p.MachinePmId) + 1, PmNo = no, MachineId = 2, ChecklistId = dto.ChecklistId, MaintenanceTypeId = type,
                ScheduledDate = Today.AddDays(-7), CompletedDate = Today.AddDays(-7), Status = MachinePmStatus.Completed,
                UpdatedAt = new DateTime(2026, 2, 22, 5, 0, 0, DateTimeKind.Utc), UpdatedBy = 3, RowVersion = new byte[] { 1 },
            });
        }

        return (s, dto.ChecklistId);
    }

    // ================================================================ create

    [Fact]
    public async Task Create_WithAMachineType_StoresIt_AndTheFirstOccurrenceCarriesIt()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(maintenanceTypeId: 1), 1, null, CancellationToken.None);

        Assert.Equal(1, dto.MaintenanceTypeId);
        Assert.Equal("Oil & Lubrication", dto.MaintenanceTypeName);
        Assert.True(dto.MaintenanceTypeIsActive);
        Assert.Equal(1, s.Repo.Stored(dto.ChecklistId).MaintenanceTypeId);
        Assert.Equal(1, Assert.Single(s.Repo.Pms, p => p.ChecklistId == dto.ChecklistId).MaintenanceTypeId);
    }

    [Fact]
    public async Task Create_WithABothType_IsAccepted()
    {
        var dto = await Create().Service.CreateAsync(New(maintenanceTypeId: 2), 1, null, CancellationToken.None);
        Assert.Equal(2, dto.MaintenanceTypeId);
    }

    [Fact]
    public async Task Create_WithoutAType_StaysOptional()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);
        Assert.Null(dto.MaintenanceTypeId);
        Assert.Null(Assert.Single(s.Repo.Pms, p => p.ChecklistId == dto.ChecklistId).MaintenanceTypeId);
    }

    [Fact]
    public async Task Create_UnknownType_IsNotFound_AndNothingIsWritten()
    {
        var s = Create();
        var before = s.Repo.Count;
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(New(maintenanceTypeId: 999), 1, null, CancellationToken.None));
        Assert.Equal(before, s.Repo.Count);
        Assert.Empty(s.Repo.Pms);
    }

    [Theory]
    [InlineData(4, "not active")]              // Machine, inactive
    [InlineData(3, "does not apply to machines")] // Mold
    public async Task Create_RefusedTypes_AreValidationErrors(int typeId, string message)
    {
        var s = Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(maintenanceTypeId: typeId), 1, null, CancellationToken.None));
        Assert.Contains(ex.Errors, e => e.Contains(message));
        Assert.Empty(s.Repo.Pms);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Create_NonPositiveType_IsAValidationError(int typeId)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create().Service.CreateAsync(New(maintenanceTypeId: typeId), 1, null, CancellationToken.None));
        Assert.Contains("MaintenanceTypeId is not valid.", ex.Errors);
    }

    [Fact]
    public async Task Create_AMoldChecklistWithAType_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create().Service.CreateAsync(New(maintenanceTypeId: 2, appliesTo: "Mold"), 1, null, CancellationToken.None));
        Assert.Contains("A Mold checklist cannot have a maintenance type.", ex.Errors);
    }

    // ================================================================ update + backfill

    [Fact]
    public async Task SettingTheType_TypesTheOpenPm_AndOnlyUntypedHistory()
    {
        var (s, id) = await PlanWithHistory();

        var dto = await s.Service.UpdateAsync(id, Edit(s, id, 1), 1, null, CancellationToken.None);

        Assert.Equal(1, dto.MaintenanceTypeId);
        Assert.Equal(1, s.Repo.StoredPm("MPM-0001").MaintenanceTypeId);   // open: follows the plan
        Assert.Equal(1, s.Repo.StoredPm("MPM-0002").MaintenanceTypeId);   // completed + untyped: filled
        Assert.Equal(2, s.Repo.StoredPm("MPM-0003").MaintenanceTypeId);   // completed + typed: history untouched
        Assert.Equal(new DateTime(2026, 2, 22, 5, 0, 0, DateTimeKind.Utc), s.Repo.StoredPm("MPM-0002").UpdatedAt); // completion evidence kept
        var entry = s.Audit.Entries.Single(e => e.Action == "ChecklistUpdated");
        Assert.Contains(entry.Details, d => d.FieldName == "maintenance_type" && d.NewValue == "Oil & Lubrication");
    }

    [Fact]
    public async Task ChangingTheType_MovesTheOpenPm_ButNeverRewritesHistory()
    {
        var (s, id) = await PlanWithHistory();
        await s.Service.UpdateAsync(id, Edit(s, id, 1), 1, null, CancellationToken.None);

        await s.Service.UpdateAsync(id, Edit(s, id, 2), 1, null, CancellationToken.None);

        Assert.Equal(2, s.Repo.StoredPm("MPM-0001").MaintenanceTypeId);
        Assert.Equal(1, s.Repo.StoredPm("MPM-0002").MaintenanceTypeId);
        Assert.Equal(2, s.Repo.StoredPm("MPM-0003").MaintenanceTypeId);
    }

    [Fact]
    public async Task ClearingTheType_ClearsItFromTheOpenPm_Only()
    {
        var (s, id) = await PlanWithHistory();
        await s.Service.UpdateAsync(id, Edit(s, id, 1), 1, null, CancellationToken.None);

        var dto = await s.Service.UpdateAsync(id, Edit(s, id, null), 1, null, CancellationToken.None);

        Assert.Null(dto.MaintenanceTypeId);
        Assert.Null(s.Repo.StoredPm("MPM-0001").MaintenanceTypeId);
        Assert.Equal(1, s.Repo.StoredPm("MPM-0002").MaintenanceTypeId);
    }

    [Fact]
    public async Task AnUnrelatedEdit_OfAnUntypedPlan_NeverClearsALegacyOpenPmType()
    {
        var (s, id) = await PlanWithHistory();
        s.Repo.StoredPm("MPM-0001").MaintenanceTypeId = 19; // a type from before migration 017

        await s.Service.UpdateAsync(id, Edit(s, id, null, name: "Press Daily Inspection v2"), 1, null, CancellationToken.None);

        Assert.Equal(19, s.Repo.StoredPm("MPM-0001").MaintenanceTypeId);
        Assert.Null(s.Repo.StoredPm("MPM-0002").MaintenanceTypeId);
        Assert.DoesNotContain(s.Audit.Entries.Single(e => e.Action == "ChecklistUpdated").Details, d => d.FieldName == "maintenance_type");
    }

    [Fact]
    public async Task AnUnchangedTypeDeactivatedSince_StaysAssigned_ButCannotBeNewlyChosen()
    {
        var (s, id) = await PlanWithHistory();
        s.Repo.Stored(id).MaintenanceTypeId = 4; // chosen while it was active

        var dto = await s.Service.UpdateAsync(id, Edit(s, id, 4), 1, null, CancellationToken.None);
        Assert.Equal(4, dto.MaintenanceTypeId);
        Assert.False(dto.MaintenanceTypeIsActive);

        var (s2, id2) = await PlanWithHistory();
        await Assert.ThrowsAsync<ValidationException>(() => s2.Service.UpdateAsync(id2, Edit(s2, id2, 4), 1, null, CancellationToken.None));
        Assert.Null(s2.Repo.StoredPm("MPM-0001").MaintenanceTypeId);
    }
}
