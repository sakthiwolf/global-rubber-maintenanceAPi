using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Migration 021 behaviour of the Machine Breakdown workflow: "Assigned To" by employee OR typed name, Reopen of a Closed
/// breakdown, the active-only list (Machine Maintenance), and the notifications every workflow event publishes; plus the
/// Machine PM scheduled / due / overdue notifications and who can see them. Machines 1 MAC-0001 Running, 2 MAC-0002.
/// Employees 1 Ravi Kumar (active), 3 Old Hand (inactive). Acting user 1.
/// </summary>
public class MachineBreakdownReopenAndNotificationTests
{
    private sealed record Sut(MachineBreakdownService Service, InMemoryMachineBreakdownRepository Repo, RecordingAuditLog Audit, SettableClock Clock, RecordingNotificationPublisher Notifications);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var repo = new InMemoryMachineBreakdownRepository(new List<MachineBreakdown>(), machineList);
        var audit = new RecordingAuditLog();
        var clock = new SettableClock(new DateOnly(2026, 10, 5)) { UtcNow = new DateTime(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc) };
        var notifications = new RecordingNotificationPublisher();
        var service = new MachineBreakdownService(repo, new InMemoryMachineRepository(machineList, departments, employees), new InMemoryBreakdownTypeLookup(),
            employees, users, clock, audit, notifications, NullLogger<MachineBreakdownService>.Instance);
        return new Sut(service, repo, audit, clock, notifications);
    }

    private static Task<MachineBreakdownDto> Report(Sut s, int machineId = 1, string? priority = null) =>
        s.Service.CreateAsync(new CreateMachineBreakdownRequest
        {
            MachineId = machineId, BreakdownDate = new DateOnly(2026, 10, 5), BreakdownTime = new TimeOnly(9, 0), Problem = "Confidential hydraulic detail", Priority = priority,
        }, 1, null, CancellationToken.None);

    private static Task<MachineBreakdownDto> Advance(Sut s, int id, string stage, int? engineer = null, string? name = null)
    {
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(5);
        return s.Service.AdvanceStageAsync(id, new AdvanceMachineBreakdownStageRequest
        {
            Stage = stage, AssignedEngineerId = engineer, AssignedToName = name, RowVersion = Convert.ToBase64String(s.Repo.Stored(id).RowVersion),
        }, 1, null, CancellationToken.None);
    }

    private static async Task<int> ClosedBreakdown(Sut s)
    {
        var id = (await Report(s)).MachineBreakdownId;
        foreach (var stage in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved, BreakdownStage.Closed })
        {
            await Advance(s, id, stage);
        }

        return id;
    }

    private static Task<MachineBreakdownDto> Reopen(Sut s, int id, string? rowVersion = null) =>
        s.Service.ReopenAsync(id, new ReopenMachineBreakdownRequest { RowVersion = rowVersion ?? Convert.ToBase64String(s.Repo.Stored(id).RowVersion) }, 1, "10.0.0.5", CancellationToken.None);

    // ================================================================ Assigned To: employee OR typed name

    [Fact]
    public async Task AssigningATypedName_StoresTheName_NotAnEmployee()
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;

        var dto = await Advance(s, id, BreakdownStage.Assigned, name: "  Contractor Suresh  ");

        Assert.Equal(("Contractor Suresh", (int?)null, (string?)null), (dto.AssignedToName, dto.AssignedEngineerId, dto.AssignedEngineerName));
        Assert.Equal("Contractor Suresh", s.Repo.Stored(id).AssignedToName);
    }

    [Fact]
    public async Task AssigningAnEmployee_KeepsTheEmployeeId_AndNoTypedName()
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;

        var dto = await Advance(s, id, BreakdownStage.Assigned, engineer: 1);

        Assert.Equal(((int?)1, "Ravi Kumar", (string?)null), (dto.AssignedEngineerId, dto.AssignedEngineerName, dto.AssignedToName));
    }

    [Fact]
    public async Task EmployeeAndTypedNameTogether_OrANameOver100_Are400()
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;

        var both = await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 1, name: "Someone"));
        Assert.Contains("Choose an employee OR type a name for Assigned To, not both.", both.Errors);
        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Assigned, name: new string('n', 101)));
        Assert.Contains("AssignedToName must be at most 100 characters.", tooLong.Errors);
        Assert.Equal(BreakdownStage.Reported, s.Repo.Stored(id).Stage);
    }

    [Fact]
    public async Task InactiveOrUnknownEmployee_IsStillRefused()
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;

        await Assert.ThrowsAsync<ValidationException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 3));
        await Assert.ThrowsAsync<NotFoundException>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 999));
    }

    // ================================================================ Reopen

    [Fact]
    public async Task Reopen_ReturnsAClosedBreakdownToResolved_KeepingItsNumberAndHistory()
    {
        var s = Create();
        var id = await ClosedBreakdown(s);
        var before = s.Repo.Stored(id);
        var snapshot = (before.BreakdownNo, before.AssignedAt, before.MaintenanceStartedAt, before.ResolvedAt, before.DowntimeHours);

        var dto = await Reopen(s, id);

        Assert.Equal((BreakdownStage.Resolved, (DateTime?)null), (dto.Stage, dto.ClosedAt));
        var after = s.Repo.Stored(id);
        Assert.Equal(snapshot, (after.BreakdownNo, after.AssignedAt, after.MaintenanceStartedAt, after.ResolvedAt, after.DowntimeHours));
        Assert.Equal(1, s.Repo.Count); // the same record - no duplicate
        var audit = s.Audit.Entries.Last();
        Assert.Equal((BreakdownAuditNames.Reopened, dto.BreakdownNo), (audit.Action, audit.RecordRef));
        Assert.Contains(audit.Details, d => d is { FieldName: "stage", OldValue: "Closed", NewValue: "Resolved" });
        Assert.Contains(audit.Details, d => d.FieldName == "closed_at" && d.OldValue != null && d.NewValue == null);
        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus); // the machine is not touched

        var closedAgain = await Advance(s, id, BreakdownStage.Closed); // the workflow continues
        Assert.Equal(BreakdownStage.Closed, closedAgain.Stage);
    }

    [Theory]
    [InlineData(BreakdownStage.Reported)]
    [InlineData(BreakdownStage.Resolved)]
    public async Task Reopen_OfABreakdownThatIsNotClosed_Is409(string stage)
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;
        if (stage == BreakdownStage.Resolved)
        {
            foreach (var next in new[] { BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted, BreakdownStage.Resolved })
            {
                await Advance(s, id, next);
            }
        }

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Reopen(s, id));
        Assert.Equal("Only a closed breakdown can be reopened.", ex.Message);
    }

    [Fact]
    public async Task Reopen_WithAStaleOrMissingRowVersion_IsRefused_AndUnknownIs404()
    {
        var s = Create();
        var id = await ClosedBreakdown(s);

        await Assert.ThrowsAsync<ConflictException>(() => Reopen(s, id, rowVersion: Convert.ToBase64String(new byte[] { 0x7F })));
        await Assert.ThrowsAsync<ValidationException>(() => s.Service.ReopenAsync(id, new ReopenMachineBreakdownRequest(), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => Reopen(s, 999, rowVersion: "AQ=="));
        Assert.Equal(BreakdownStage.Closed, s.Repo.Stored(id).Stage);
    }

    // ================================================================ notifications

    [Fact]
    public async Task EveryWorkflowEvent_PublishesOneNotification_ToTheBreakdownModule()
    {
        var s = Create();
        var id = await ClosedBreakdown(s);
        await Reopen(s, id);
        await Advance(s, id, BreakdownStage.Closed);

        Assert.Equal(
            new[]
            {
                NotificationTypes.BreakdownReported, NotificationTypes.BreakdownAssigned, NotificationTypes.BreakdownStarted,
                NotificationTypes.BreakdownResolved, NotificationTypes.BreakdownClosed, NotificationTypes.BreakdownReopened, NotificationTypes.BreakdownClosed,
            },
            s.Notifications.Published.Select(n => n.NotificationType));
        Assert.All(s.Notifications.Published, n =>
        {
            Assert.Equal(ModuleCodes.TrnMachineBreakdown, n.ModuleCode);
            Assert.Equal($"/transactions/machine-breakdown?open={id}", n.LinkPath);
            Assert.Equal("BRK-0001", n.RecordRef);
            Assert.DoesNotContain("Confidential", n.Message); // the problem text is never broadcast
            Assert.True(NotificationTypes.All.Contains(n.NotificationType));
            Assert.True(n.NotificationType.Length <= 30 && n.EventKey.Length <= 100);
        });
        Assert.Equal(s.Notifications.Published.Count, s.Notifications.Published.Select(n => n.EventKey).Distinct().Count()); // the second close is a new event
        Assert.Equal("BRK-0001 reported for MAC-0001 - Injection Moulding M/c 1. Priority: Medium.", s.Notifications.Published[0].Message);
    }

    [Theory]
    [InlineData("Critical", NotificationSeverity.Critical)]
    [InlineData("High", NotificationSeverity.Warning)]
    [InlineData("Medium", NotificationSeverity.Info)]
    [InlineData("Low", NotificationSeverity.Info)]
    public async Task TheReportedNotification_SeverityFollowsThePriority(string priority, string severity)
    {
        var s = Create();
        await Report(s, priority: priority);
        Assert.Equal(severity, Assert.Single(s.Notifications.Published).Severity);
    }

    [Fact]
    public async Task AFailedOperation_PublishesNothing()
    {
        var s = Create();
        var id = (await Report(s)).MachineBreakdownId;
        s.Notifications.Published.Clear();

        await Assert.ThrowsAnyAsync<Exception>(() => Advance(s, id, BreakdownStage.Resolved));          // skipped stage
        await Assert.ThrowsAnyAsync<Exception>(() => Advance(s, id, BreakdownStage.Assigned, engineer: 999)); // unknown engineer
        s.Repo.FailNextSave = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Report(s));

        Assert.Empty(s.Notifications.Published);
    }

    // ================================================================ active breakdowns (Machine Maintenance)

    [Fact]
    public async Task ActiveOnly_ListsEveryBreakdownThatIsNotClosed()
    {
        var s = Create();
        var closed = await ClosedBreakdown(s);
        var open = (await Report(s, machineId: 2)).MachineBreakdownId;

        var active = await s.Service.GetAllAsync(new MachineBreakdownListQuery { PageNumber = 1, PageSize = 100, ActiveOnly = true }, CancellationToken.None);

        Assert.Equal(new[] { open }, active.Items.Select(b => b.MachineBreakdownId));
        Assert.Equal(MachineOperationalStatus.Breakdown, active.Items[0].MachineOperationalStatus);
        Assert.DoesNotContain(active.Items, b => b.MachineBreakdownId == closed);
    }

    // ================================================================ Machine PM notifications

    private sealed class FakeDueQuery : IMachinePmNotificationQuery
    {
        public List<MachinePmNotificationCandidate> Candidates { get; } = new();
        public Task<IReadOnlyList<MachinePmNotificationCandidate>> GetUnnotifiedDueAsync(DateOnly today, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MachinePmNotificationCandidate>>(Candidates.Where(c => c.ScheduledDate <= today).Take(maxCount).ToList());
    }

    [Fact]
    public async Task TheScanner_NotifiesDueToday_AndOverdue_OncePerOccurrence()
    {
        var query = new FakeDueQuery();
        query.Candidates.Add(new MachinePmNotificationCandidate(21, "MPM-0021", "MAC-0003", new DateOnly(2026, 10, 5)));
        query.Candidates.Add(new MachinePmNotificationCandidate(19, "MPM-0019", "MAC-0001", new DateOnly(2026, 10, 1)));
        var publisher = new RecordingNotificationPublisher();
        var scanner = new MachinePmNotificationScanner(query, publisher, new SettableClock(new DateOnly(2026, 10, 5)));

        Assert.Equal(2, await scanner.ScanAsync(CancellationToken.None));
        Assert.Equal(0, await scanner.ScanAsync(CancellationToken.None)); // already notified - nothing new

        var due = publisher.Published.Single(n => n.NotificationType == NotificationTypes.MachinePmDue);
        Assert.Equal(("MachinePmDue:21", "MPM-0021", ModuleCodes.TrnMachinePm, "/transactions/machine-maintenance"), (due.EventKey, due.RecordRef, due.ModuleCode, due.LinkPath));
        Assert.Equal("MPM-0021 is due today for MAC-0003.", due.Message);
        var overdue = publisher.Published.Single(n => n.NotificationType == NotificationTypes.MachinePmOverdue);
        Assert.Equal(("MachinePmOverdue:19", NotificationSeverity.Warning), (overdue.EventKey, overdue.Severity));
    }

    [Fact]
    public async Task CreatingAMachinePlan_PublishesPmScheduled_ForItsFirstOccurrence()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var publisher = new RecordingNotificationPublisher();
        var checklists = new MaintenanceChecklistService(new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.All(), machineList),
            new InMemoryMachineRepository(machineList, departments, employees), new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes()), users,
            new FixedClock(), new RecordingAuditLog(), publisher, NullLogger<MaintenanceChecklistService>.Instance);

        await checklists.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = "Press weekly", AppliesTo = "Machine", Frequency = "Weekly", MachineId = 1, StartDate = new DateOnly(2026, 3, 2),
            SourceChecklistId = MaintenanceChecklistTestData.DefaultMasterId,
        }, 1, null, CancellationToken.None);

        var n = Assert.Single(publisher.Published);
        Assert.Equal((NotificationTypes.MachinePmScheduled, ModuleCodes.TrnMachinePm, "MPM-0001"), (n.NotificationType, n.ModuleCode, n.RecordRef));
        Assert.Equal("MPM-0001 is scheduled for MAC-0001 on 2026-03-02.", n.Message);
    }

    // ================================================================ who sees them

    private sealed class StubPermissions : IPermissionService
    {
        private readonly Dictionary<int, string[]> _viewable;
        public StubPermissions(Dictionary<int, string[]> viewable) => _viewable = viewable;
        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest r, int? u, string? ip, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken c) => Task.FromResult(new RolePermissionMatrixDto
        {
            Permissions = _viewable.GetValueOrDefault(userId, Array.Empty<string>()).Select(m => new ModulePermissionDto { ModuleCode = m, CanView = true }).ToList(),
        });
    }

    [Fact]
    public async Task BreakdownNotifications_ReachOnlyUsersWhoCanViewMachineBreakdown_WithPerUserReadState()
    {
        var s = Create();
        var store = new InMemoryNotificationRepository();
        var realPublisher = new NotificationPublisher(store, NullLogger<NotificationPublisher>.Instance);
        await realPublisher.PublishAsync(new NewNotification(NotificationTypes.BreakdownReported, ModuleCodes.TrnMachineBreakdown, NotificationSeverity.Info,
            "New breakdown reported", "BRK-0001 reported for MAC-0001.", "BreakdownReported:1"), CancellationToken.None);
        await realPublisher.PublishAsync(new NewNotification(NotificationTypes.MachinePmDue, ModuleCodes.TrnMachinePm, NotificationSeverity.Info,
            "Preventive maintenance due", "MPM-0001 is due today for MAC-0001.", "MachinePmDue:1"), CancellationToken.None);
        var service = new NotificationService(store, new StubPermissions(new()
        {
            [1] = new[] { ModuleCodes.TrnMachineBreakdown, ModuleCodes.TrnMachinePm }, // e.g. maintenance manager
            [2] = new[] { ModuleCodes.TrnMachinePm },                                   // PM only
            [3] = new[] { ModuleCodes.MasterProduct },                                  // e.g. production user
        }), new FixedClock(), new NotificationChangeSignal());

        Assert.Equal(2, (await service.GetMyUnreadCountAsync(1, CancellationToken.None)).UnreadCount);
        var two = await service.GetMyNotificationsAsync(2, new NotificationListQuery { PageNumber = 1, PageSize = 20 }, CancellationToken.None);
        Assert.Equal(new[] { NotificationTypes.MachinePmDue }, two.Items.Select(n => n.NotificationType));
        Assert.Equal(0, (await service.GetMyUnreadCountAsync(3, CancellationToken.None)).UnreadCount);
        var breakdownId = store.Items.Single(n => n.NotificationType == NotificationTypes.BreakdownReported).NotificationId;
        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkAsReadAsync(2, breakdownId, CancellationToken.None)); // not theirs

        await service.MarkAsReadAsync(1, breakdownId, CancellationToken.None);
        Assert.Equal(1, (await service.GetMyUnreadCountAsync(1, CancellationToken.None)).UnreadCount);
        await service.MarkAllAsReadAsync(1, CancellationToken.None);
        Assert.Equal(0, (await service.GetMyUnreadCountAsync(1, CancellationToken.None)).UnreadCount);
        Assert.Equal(1, (await service.GetMyUnreadCountAsync(2, CancellationToken.None)).UnreadCount); // read state is per user
    }
}
