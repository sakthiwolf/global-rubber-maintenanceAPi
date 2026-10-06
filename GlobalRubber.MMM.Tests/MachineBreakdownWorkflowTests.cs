using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The full Machine Breakdown workflow through the real MachineBreakdownService (in-memory persistence): every stage
/// transition with its timestamps, downtime, engineer assignment, Closed protection, length / paging validation and the
/// machine status rules BR-16 / BR-20. Machines: 1 MAC-0001 Running, 2 MAC-0002 Breakdown (seeded), 3 inactive.
/// Employees: 1 Ravi Kumar (active), 2 Karthik Raja (active), 3 Old Hand (INACTIVE). Acting user 1.
/// </summary>
public class MachineBreakdownWorkflowTests
{
    private sealed record Sut(MachineBreakdownService Service, InMemoryMachineBreakdownRepository Repo, RecordingAuditLog Audit, SettableClock Clock);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var machines = new InMemoryMachineRepository(machineList, departments, employees);
        var repo = new InMemoryMachineBreakdownRepository(new List<MachineBreakdown>(), machineList);
        var audit = new RecordingAuditLog();
        var clock = new SettableClock(new DateOnly(2026, 10, 5)) { UtcNow = new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc) };
        var service = new MachineBreakdownService(repo, machines, new InMemoryBreakdownTypeLookup(), employees, users, clock, audit, new RecordingNotificationPublisher(), NullLogger<MachineBreakdownService>.Instance);
        return new Sut(service, repo, audit, clock);
    }

    private static CreateMachineBreakdownRequest Report(int machineId = 1) => new()
    {
        MachineId = machineId, BreakdownDate = new DateOnly(2026, 10, 5), BreakdownTime = new TimeOnly(9, 0), Problem = "Hydraulic leak",
    };

    private static AdvanceMachineBreakdownStageRequest To(Sut s, int id, string stage, int? engineer = null, string? rootCause = null, string? corrective = null, string? rowVersion = null) => new()
    {
        Stage = stage, AssignedEngineerId = engineer, RootCause = rootCause, CorrectiveAction = corrective,
        RowVersion = rowVersion ?? Convert.ToBase64String(s.Repo.Stored(id).RowVersion),
    };

    private static Task<MachineBreakdownDto> Advance(Sut s, int id, string stage, int? engineer = null, string? rootCause = null, string? corrective = null) =>
        s.Service.AdvanceStageAsync(id, To(s, id, stage, engineer, rootCause, corrective), 1, null, CancellationToken.None);

    // ================================================================ every stage, timestamps, downtime

    [Fact]
    public async Task TheFullWorkflow_SetsEachTimestamp_AndTheDowntime()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;

        s.Clock.UtcNow = new DateTime(2026, 10, 5, 4, 10, 0, DateTimeKind.Utc);
        var assigned = await Advance(s, id, BreakdownStage.Assigned, engineer: 1);
        Assert.Equal((BreakdownStage.Assigned, (int?)1, "Ravi Kumar"), (assigned.Stage, assigned.AssignedEngineerId, assigned.AssignedEngineerName));
        Assert.Equal(s.Clock.UtcNow, assigned.AssignedAt);

        s.Clock.UtcNow = new DateTime(2026, 10, 5, 4, 30, 0, DateTimeKind.Utc);
        var started = await Advance(s, id, BreakdownStage.MaintenanceStarted);
        Assert.Equal(s.Clock.UtcNow, started.MaintenanceStartedAt);
        Assert.Null(started.DowntimeHours);

        s.Clock.UtcNow = new DateTime(2026, 10, 5, 7, 14, 0, DateTimeKind.Utc); // 2 h 44 min after the start
        var resolved = await Advance(s, id, BreakdownStage.Resolved, rootCause: " Seal worn ", corrective: "Seal replaced");
        Assert.Equal(s.Clock.UtcNow, resolved.ResolvedAt);
        Assert.Equal(2.7m, resolved.DowntimeHours); // Resolved - Maintenance Started, one decimal
        Assert.Equal(("Seal worn", "Seal replaced"), (resolved.RootCause, resolved.CorrectiveAction));

        s.Clock.UtcNow = new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc);
        var closed = await Advance(s, id, BreakdownStage.Closed);
        Assert.Equal((BreakdownStage.Closed, (DateTime?)s.Clock.UtcNow), (closed.Stage, closed.ClosedAt));
        Assert.Equal(2.7m, closed.DowntimeHours);                         // unchanged by closing
        Assert.Equal(new DateTime(2026, 10, 5, 4, 10, 0, DateTimeKind.Utc), closed.AssignedAt); // earlier timestamps are kept
        Assert.Equal(4, s.Audit.Entries.Count(e => e.Action == BreakdownAuditNames.StageChanged));
    }

    [Fact]
    public async Task Downtime_IsRoundedToOneDecimal_AndNeverNegative()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;
        await Advance(s, id, BreakdownStage.Assigned);
        await Advance(s, id, BreakdownStage.MaintenanceStarted);

        s.Clock.UtcNow = s.Clock.UtcNow.AddSeconds(-30); // a clock that moved backwards
        var resolved = await Advance(s, id, BreakdownStage.Resolved);

        Assert.Equal(0m, resolved.DowntimeHours);
    }

    // ================================================================ Closed protection / invalid transitions

    [Theory]
    [InlineData(BreakdownStage.Closed)]
    [InlineData(BreakdownStage.Reported)]
    [InlineData(BreakdownStage.Resolved)]
    public async Task AClosedBreakdown_CannotChangeAnyMore_409(string stage)
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;
        foreach (var next in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved, BreakdownStage.Closed })
        {
            await Advance(s, id, next);
        }

        var before = s.Repo.Stored(id).RowVersion.ToArray();
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Advance(s, id, stage));

        Assert.Equal("The breakdown is already closed and cannot be changed.", ex.Message);
        Assert.Equal(before, s.Repo.Stored(id).RowVersion); // nothing written
    }

    [Fact]
    public async Task SkippingOrGoingBack_IsStillRefusedWith400()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;
        await Advance(s, id, BreakdownStage.Assigned);

        await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Resolved));
        await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Reported));
        Assert.Equal(BreakdownStage.Assigned, s.Repo.Stored(id).Stage);
    }

    [Fact]
    public async Task AStaleRowVersion_Is409_AndNothingIsWritten()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;
        var stale = Convert.ToBase64String(s.Repo.Stored(id).RowVersion);
        await Advance(s, id, BreakdownStage.Assigned);

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.AdvanceStageAsync(id, To(s, id, BreakdownStage.MaintenanceStarted, rowVersion: stale), 1, null, CancellationToken.None));
        Assert.Equal(BreakdownStage.Assigned, s.Repo.Stored(id).Stage);
    }

    // ================================================================ engineer assignment

    [Fact]
    public async Task Assign_WithoutAnEngineer_IsAllowed()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;

        var dto = await Advance(s, id, BreakdownStage.Assigned);

        Assert.Equal((BreakdownStage.Assigned, (int?)null), (dto.Stage, dto.AssignedEngineerId));
    }

    [Fact]
    public async Task Assign_UnknownEngineer_Is404_InactiveEngineer_Is400_InvalidId_Is400_NothingWritten()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;

        await Assert.ThrowsAsync<NotFoundException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 999));
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 3));
        Assert.Contains("The selected engineer is not active.", inactive.Errors);
        var invalid = await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 0));
        Assert.Contains("AssignedEngineerId is not valid.", invalid.Errors);

        Assert.Equal((BreakdownStage.Reported, (int?)null), (s.Repo.Stored(id).Stage, s.Repo.Stored(id).AssignedEngineerId));
    }

    // ================================================================ length validation

    [Fact]
    public async Task RootCauseAndCorrectiveAction_Over1000Characters_Are400_And1000IsAccepted()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(), 1, null, CancellationToken.None)).MachineBreakdownId;
        await Advance(s, id, BreakdownStage.Assigned);
        await Advance(s, id, BreakdownStage.MaintenanceStarted);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Advance(s, id, BreakdownStage.Resolved, rootCause: new string('r', 1001), corrective: new string('c', 1001)));
        Assert.Contains("RootCause must be at most 1000 characters.", ex.Errors);
        Assert.Contains("CorrectiveAction must be at most 1000 characters.", ex.Errors);
        Assert.Equal(BreakdownStage.MaintenanceStarted, s.Repo.Stored(id).Stage);

        var ok = await Advance(s, id, BreakdownStage.Resolved, rootCause: new string('r', 1000), corrective: new string('c', 1000));
        Assert.Equal(1000, ok.RootCause!.Length);
    }

    // ================================================================ paging

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task InvalidPaging_Is400(int pageNumber, int pageSize)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create().Service.GetAllAsync(new MachineBreakdownListQuery { PageNumber = pageNumber, PageSize = pageSize }, CancellationToken.None));
        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public async Task PageSize100_IsAccepted()
    {
        var page = await Create().Service.GetAllAsync(new MachineBreakdownListQuery { PageNumber = 1, PageSize = 100 }, CancellationToken.None);
        Assert.Equal(100, page.PageSize);
    }

    // ================================================================ machine status (BR-16 / BR-20)

    [Fact]
    public async Task Reporting_PutsTheMachineIntoBreakdown_AndAuditsIt()
    {
        var s = Create();

        await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None);

        Assert.Equal(MachineOperationalStatus.Breakdown, s.Repo.StoredMachine(1).OperationalStatus);
        Assert.Equal(1, s.Repo.StoredMachine(1).UpdatedBy);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains(entry.Details, d => d is { FieldName: "machine_operational_status (MAC-0001)", OldValue: "Running", NewValue: "Breakdown" });
        Assert.Contains("Machine MAC-0001 status Running -> Breakdown.", entry.Description);
    }

    [Fact]
    public async Task AFailedReport_LeavesTheMachineStatusAlone()
    {
        var s = Create();
        s.Repo.FailNextSave = true;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None));

        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus);
    }

    [Fact]
    public async Task Resolving_ReturnsTheMachineToRunning_WhenNoOtherBreakdownIsUnresolved()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None)).MachineBreakdownId;
        await Advance(s, id, BreakdownStage.Assigned);
        await Advance(s, id, BreakdownStage.MaintenanceStarted);
        Assert.Equal(MachineOperationalStatus.Breakdown, s.Repo.StoredMachine(1).OperationalStatus); // still down while being repaired

        await Advance(s, id, BreakdownStage.Resolved);

        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus);
        Assert.Contains(s.Audit.Entries.Last().Details, d => d is { FieldName: "machine_operational_status (MAC-0001)", OldValue: "Breakdown", NewValue: "Running" });

        await Advance(s, id, BreakdownStage.Closed); // closing does not touch the machine again
        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus);
        Assert.Empty(s.Audit.Entries.Last().Details);
    }

    [Fact]
    public async Task Resolving_OneOfTwoOpenBreakdowns_KeepsTheMachineInBreakdown()
    {
        var s = Create();
        var first = (await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None)).MachineBreakdownId;
        var second = (await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None)).MachineBreakdownId;
        foreach (var next in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved })
        {
            await Advance(s, first, next);
        }

        Assert.Equal(MachineOperationalStatus.Breakdown, s.Repo.StoredMachine(1).OperationalStatus); // the second is still open
        Assert.Empty(s.Audit.Entries.Last().Details);

        foreach (var next in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved })
        {
            await Advance(s, second, next);
        }

        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus);
    }

    [Fact]
    public async Task Resolving_LeavesAStatusThisModuleDidNotSet_Alone()
    {
        var s = Create();
        var id = (await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None)).MachineBreakdownId;
        await Advance(s, id, BreakdownStage.Assigned);
        await Advance(s, id, BreakdownStage.MaintenanceStarted);
        s.Repo.StoredMachine(1).OperationalStatus = MachineOperationalStatus.Idle; // changed elsewhere meanwhile

        await Advance(s, id, BreakdownStage.Resolved);

        Assert.Equal(MachineOperationalStatus.Idle, s.Repo.StoredMachine(1).OperationalStatus);
    }

    [Fact]
    public async Task TheLastMaintenanceDate_IsNeverChangedByABreakdown()
    {
        var s = Create();
        var before = s.Repo.StoredMachine(1).LastMaintenanceDate;
        var id = (await s.Service.CreateAsync(Report(machineId: 1), 1, null, CancellationToken.None)).MachineBreakdownId;
        foreach (var next in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved, BreakdownStage.Closed })
        {
            await Advance(s, id, next);
        }

        Assert.Equal(before, s.Repo.StoredMachine(1).LastMaintenanceDate); // open question Q-20
    }
}
