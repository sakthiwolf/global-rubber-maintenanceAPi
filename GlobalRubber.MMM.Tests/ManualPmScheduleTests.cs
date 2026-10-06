using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Manual / One-Time preventive maintenance (migration 025) - the real ManualPmService, MachinePmService and MoldPmService
/// against in-memory fakes. Machines (MachineTestData): 1 MAC-0001, 2 MAC-0002 active; 3 inactive. Molds (MoldTestData):
/// 1 MLD-0001 (usage-based PM: interval 50,000, cycle start 100,000, usage 100,000), 2 MLD-0002, 3 MLD-0003 Retired.
/// Maintenance types: 1 Machine, 2 Both (active), 3 Mold (inactive). Checklist Masters: 901 Machine (2 items), 902 Machine
/// inactive, 903 Mold (1 item), 904 Machine without items; plan 1 (Daily, MAC-0001).
/// </summary>
public class ManualPmScheduleTests : IClassFixture<ApiWebApplicationFactory>
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);
    private static readonly DateOnly Today = D(2026, 10, 6);

    private readonly ApiWebApplicationFactory _factory;

    public ManualPmScheduleTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Sut(
        ManualPmService Manual, MachinePmService MachinePms, MoldPmService MoldPms, MachinePmScenario Data, InMemoryMachinePmRepository MachinePmRepo,
        InMemoryMoldPmRepository MoldPmRepo, List<Mold> Molds, InMemoryMaintenanceChecklistRepository Checklists, RecordingAuditLog Audit,
        RecordingNotificationPublisher Notifications, SettableClock Clock);

    private static Sut Create(MachinePmScenario? data = null)
    {
        data ??= new MachinePmScenario();
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var molds = MoldTestData.Molds();
        var moldRepo = new InMemoryMoldRepository(molds, products, employees);
        var checklists = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.All());
        var types = new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes());
        var machinePmRepo = new InMemoryMachinePmRepository(data);
        var moldPmRepo = new InMemoryMoldPmRepository(molds);
        var audit = new RecordingAuditLog();
        var notifications = new RecordingNotificationPublisher();
        var clock = new SettableClock(Today);
        var machinePms = new MachinePmService(machinePmRepo, users, clock, audit, notifications, NullLogger<MachinePmService>.Instance);
        var evaluator = MoldPmTestFactory.Evaluator(moldPmRepo, new InMemoryNotificationRepository(), clock, audit);
        var moldPms = new MoldPmService(moldPmRepo, evaluator, users, clock, audit, NullLogger<MoldPmService>.Instance);
        var manual = new ManualPmService(machinePmRepo, moldPmRepo, machinePms, moldPms, machines, moldRepo, types, checklists, users, clock, audit,
            notifications, NullLogger<ManualPmService>.Instance);
        return new Sut(manual, machinePms, moldPms, data, machinePmRepo, moldPmRepo, molds, checklists, audit, notifications, clock);
    }

    private static ScheduleManualMachinePmRequest MachineRequest(
        string? title = "Spindle noise check", int? machineId = 1, DateOnly? date = null, int? typeId = null, int? masterId = MaintenanceChecklistTestData.DefaultMasterId) =>
        new() { Title = title, MachineId = machineId, MaintenanceDate = date ?? D(2026, 10, 15), MaintenanceTypeId = typeId, SourceChecklistId = masterId };

    private static ScheduleManualMoldPmRequest MoldRequest(string? title = "Cavity polish", int? moldId = 1, DateOnly? date = null, int? masterId = null) =>
        new() { Title = title, MoldId = moldId, MaintenanceDate = date ?? D(2026, 10, 15), SourceChecklistId = masterId };

    private static Task<MachinePmDto> ScheduleMachine(Sut s, ScheduleManualMachinePmRequest r) => s.Manual.ScheduleMachineAsync(r, 1, "10.0.0.5", CancellationToken.None);
    private static Task<MoldPmDto> ScheduleMold(Sut s, ScheduleManualMoldPmRequest r) => s.Manual.ScheduleMoldAsync(r, 1, "10.0.0.5", CancellationToken.None);

    // ================================================================ 2 + 4 + 5 + 7: machine - exactly one PM, nothing else

    [Fact]
    public async Task ManualMachinePm_CreatesExactlyOnePm_WithTheChecklistSnapshot_AndNothingElse()
    {
        var data = new MachinePmScenario();
        var plan = data.Checklist("Daily", D(2026, 10, 1));
        var planPm = data.Open(plan, D(2026, 10, 7));
        var planBefore = (plan.Frequency, plan.StartDate, plan.MachineId, plan.IsActive);
        var s = Create(data);

        var dto = await ScheduleMachine(s, MachineRequest(title: "  Spindle noise check  ", typeId: 2));

        var created = Assert.Single(s.Data.Pms, p => p.ScheduleType == PmScheduleType.Manual);
        Assert.Equal(2, s.Data.Pms.Count); // the plan's own occurrence + exactly ONE manual PM
        Assert.Equal((PmScheduleType.Manual, "Spindle noise check", (int?)null, MachinePmStatus.Scheduled, D(2026, 10, 15), 1, (int?)2),
            (created.ScheduleType, created.Title, created.ChecklistId, created.Status, created.ScheduledDate, created.MachineId, created.MaintenanceTypeId));
        Assert.Equal(new[] { "Hoses checked", "Pressure recorded" }, created.ChecklistItems.OrderBy(i => i.SortOrder).Select(i => i.ItemLabel)); // master 901
        Assert.All(created.ChecklistItems, i => Assert.False(i.IsChecked));
        Assert.Equal((PmScheduleType.Manual, "Spindle noise check", "MPM-0002", (int?)2), (dto.ScheduleType, dto.Title, dto.PmNo, dto.MaintenanceTypeId));
        Assert.Equal(2, dto.ChecklistItems.Count);
        Assert.Null(dto.Frequency);
        // 7: the recurring plan and its occurrence are untouched.
        Assert.Equal(planBefore, (plan.Frequency, plan.StartDate, plan.MachineId, plan.IsActive));
        Assert.Equal((MachinePmStatus.Scheduled, D(2026, 10, 7)), (s.Data.Pms.Single(p => p.MachinePmId == planPm.MachinePmId).Status, planPm.ScheduledDate));
    }

    [Fact] // 6
    public async Task ManualMachinePm_DoesNotChangeTheMachinesMaintenanceDatesOrFrequency_NotEvenWhenCompleted()
    {
        var s = Create();
        var machine = s.Data.Machines.Single(m => m.MachineId == 1);
        var before = (machine.LastMaintenanceDate, machine.NextMaintenanceDate, machine.MaintenanceFrequencyDays);

        var dto = await ScheduleMachine(s, MachineRequest(date: Today));
        Assert.Equal(before, (machine.LastMaintenanceDate, machine.NextMaintenanceDate, machine.MaintenanceFrequencyDays));

        var pm = s.Data.Pms.Single();
        await s.MachinePms.CompleteAsync(pm.MachinePmId,
            new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);

        Assert.Equal(MachinePmStatus.Completed, pm.Status);
        Assert.Equal(before, (machine.LastMaintenanceDate, machine.NextMaintenanceDate, machine.MaintenanceFrequencyDays));
    }

    [Fact] // 5
    public async Task CompletingAManualMachinePm_NeverCreatesASuccessor()
    {
        var s = Create();
        var dto = await ScheduleMachine(s, MachineRequest(date: Today));

        var completed = await s.MachinePms.CompleteAsync(dto.MachinePmId,
            new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);

        Assert.Single(s.Data.Pms);
        Assert.Equal(MachinePmStatus.Completed, completed.Status);
        Assert.DoesNotContain(s.Audit.Entries, e => e.Action == MachinePmAuditNames.Scheduled);
    }

    [Fact] // 1: the automatic flow still schedules its successor, and the manual PM does not move the machine's next date
    public async Task AutomaticCompletion_StillCreatesItsSuccessor_AndIgnoresAnOpenManualPmForTheNextDate()
    {
        var data = new MachinePmScenario();
        var plan = data.Checklist("Weekly", D(2026, 9, 29));
        var occurrence = data.Open(plan, D(2026, 10, 6));
        var s = Create(data);
        await ScheduleMachine(s, MachineRequest(date: D(2026, 10, 8))); // earlier than the next automatic date

        await s.MachinePms.CompleteAsync(occurrence.MachinePmId,
            new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = Convert.ToBase64String(occurrence.RowVersion) }, 1, null, CancellationToken.None);

        var successor = Assert.Single(s.Data.Pms, p => p.ChecklistId == plan.ChecklistId && p.Status == MachinePmStatus.Scheduled);
        Assert.Equal(D(2026, 10, 13), successor.ScheduledDate);
        var machine = s.Data.Machines.Single(m => m.MachineId == 1);
        Assert.Equal((Today, D(2026, 10, 13)), (machine.LastMaintenanceDate!.Value, machine.NextMaintenanceDate!.Value)); // not 10-08
    }

    // ================================================================ 3: mold

    [Fact]
    public async Task ManualMoldPm_CreatesOneScheduledPm_OutsideTheUsageCycle()
    {
        var s = Create();
        var mold = s.Molds.Single(m => m.MoldId == 1);
        var before = (mold.PmCycleStartShots, mold.CurrentUsageShots, mold.Status, mold.MaintenanceFrequencyShots);

        var dto = await ScheduleMold(s, MoldRequest(masterId: MaintenanceChecklistTestData.MoldMasterId));

        var created = Assert.Single(s.MoldPmRepo.Pms);
        Assert.Equal((PmScheduleType.Manual, "Cavity polish", MoldPmCategory.Scheduled, MoldPmStatus.Scheduled, D(2026, 10, 15), (int?)null, 100000),
            (created.ScheduleType, created.Title, created.Category, created.Status, created.ScheduledDate, created.ThresholdShots, created.MoldUsageAtService));
        Assert.Equal(new[] { "Cavity cleaned" }, created.ChecklistItems.Select(i => i.ItemLabel));
        Assert.Equal((PmScheduleType.Manual, MoldPmAuditNames.ManualTrigger, 1), (dto.ScheduleType, dto.Trigger, dto.ChecklistItems.Count));
        Assert.Null(dto.RemainingShots);
        Assert.Equal(before, (mold.PmCycleStartShots, mold.CurrentUsageShots, mold.Status, mold.MaintenanceFrequencyShots));
    }

    [Fact]
    public async Task CompletingAManualMoldPm_KeepsTheUsageCycle_RecordsTheTicks_AndReleasesMaintenance()
    {
        var s = Create();
        var mold = s.Molds.Single(m => m.MoldId == 1);
        mold.CurrentUsageShots = 130000; // mid-cycle (next threshold 150,000)
        var dto = await ScheduleMold(s, MoldRequest(date: Today, masterId: MaintenanceChecklistTestData.MoldMasterId));
        var started = await s.MoldPms.StartAsync(dto.MoldPmId, new StartMoldPmRequest { RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);
        Assert.Equal(MoldStatus.Maintenance, mold.Status);

        var completed = await s.MoldPms.CompleteAsync(dto.MoldPmId, new CompleteMoldPmRequest
        {
            MaintenanceBy = "Ravi", RowVersion = started.RowVersion,
            Results = new[] { new MoldPmChecklistResultRequest { MoldPmChecklistId = dto.ChecklistItems[0].MoldPmChecklistId, IsChecked = true } },
        }, 1, null, CancellationToken.None);

        Assert.Equal(100000, mold.PmCycleStartShots); // NOT re-anchored
        Assert.Equal(MoldStatus.InProduction, mold.Status);
        Assert.Equal((MoldPmStatus.Completed, 130000), (completed.Status, completed.UsageAtCompletion!.Value));
        Assert.True(Assert.Single(s.MoldPmRepo.Pms).ChecklistItems.Single().IsChecked);
        Assert.Contains("usage-based maintenance cycle is unchanged", s.Audit.Entries.Last().Description);
    }

    [Fact]
    public async Task AManualMoldPm_DoesNotBlockOrChangeTheAutomaticUsagePm()
    {
        var s = Create();
        await ScheduleMold(s, MoldRequest(date: Today));

        Assert.False(await s.MoldPmRepo.HasOpenAutomaticPmAsync(1, CancellationToken.None));
        var usage = (await s.MoldPms.GetMoldUsageAsync(CancellationToken.None)).Single(u => u.MoldId == 1);
        Assert.Null(usage.OpenPmNo); // the mold's usage-based state ignores the manual PM
    }

    [Fact] // the Mold "Due" tab: due today only - a future manual PM waits, like a future machine PM
    public async Task MoldDueTab_ShowsTodaysPmOnly_FutureManualPmWaits()
    {
        var s = Create();
        var future = await ScheduleMold(s, MoldRequest(title: "Future", date: D(2026, 10, 20)));
        var today = await ScheduleMold(s, MoldRequest(title: "Today", moldId: 2, date: Today));

        var due = await s.MoldPms.GetAllAsync(new MoldPmListQuery { PageNumber = 1, PageSize = 10, Bucket = MoldPmBucket.Due }, CancellationToken.None);
        var counts = await s.MoldPms.GetCountsAsync(new MoldPmListQuery(), CancellationToken.None);

        Assert.Equal(new[] { today.PmNo }, due.Items.Select(i => i.PmNo));
        Assert.Equal((1, 0), (counts.Due, counts.Overdue));
        Assert.Contains((await s.MoldPms.GetAllAsync(new MoldPmListQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None)).Items, i => i.PmNo == future.PmNo);
    }

    // ================================================================ the One-Time tab

    [Fact]
    public async Task OneTimeTab_ListsDueOpenManualPms_AndTheFrequencyTabsDoNot()
    {
        var data = new MachinePmScenario();
        var plan = data.Checklist("Daily", D(2026, 10, 1));
        data.Open(plan, Today);
        var s = Create(data);
        var overdue = await ScheduleMachine(s, MachineRequest(title: "Overdue", date: D(2026, 10, 1)));
        var due = await ScheduleMachine(s, MachineRequest(title: "Today", date: Today));
        await ScheduleMachine(s, MachineRequest(title: "Future", date: D(2026, 10, 30)));

        var oneTime = await s.MachinePms.GetAllAsync(new MachinePmListQuery { PageNumber = 1, PageSize = 10, Bucket = MachinePmBucket.OneTime }, CancellationToken.None);
        var daily = await s.MachinePms.GetAllAsync(new MachinePmListQuery { PageNumber = 1, PageSize = 10, Bucket = MachinePmBucket.Daily }, CancellationToken.None);
        var counts = await s.MachinePms.GetBucketCountsAsync(new MachinePmListQuery(), CancellationToken.None);

        Assert.Equal(new[] { overdue.PmNo, due.PmNo }, oneTime.Items.Select(i => i.PmNo));
        Assert.True(oneTime.Items[0].IsOverdue);
        Assert.All(daily.Items, i => Assert.Equal(PmScheduleType.Automatic, i.ScheduleType));
        Assert.Equal((2, 1), (counts.OneTime, counts.Daily));
        var search = await s.MachinePms.GetAllAsync(new MachinePmListQuery { PageNumber = 1, PageSize = 10, Search = "future" }, CancellationToken.None);
        Assert.Equal("Future", Assert.Single(search.Items).Title);
    }

    // ================================================================ 8 - 13: validation

    [Fact]
    public async Task Manual_RequiresTitleTargetAndDate_FrequencyIsNotPartOfIt()
    {
        var s = Create();

        var machine = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s,
            new ScheduleManualMachinePmRequest { Title = "  ", MachineId = null, MaintenanceDate = null }));
        Assert.Equal(new[] { "Title is required.", "MaintenanceDate is required.", "MachineId is required." }, machine.Errors);

        var mold = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMold(s, new ScheduleManualMoldPmRequest { Title = "x", MoldId = 0 }));
        Assert.Equal(new[] { "MaintenanceDate is required.", "MoldId is required." }, mold.Errors);

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(title: new string('t', 151))));
        Assert.Contains("Title must be at most 150 characters.", tooLong.Errors);

        Assert.Empty(s.Data.Pms);
        Assert.Empty(s.MoldPmRepo.Pms);
        Assert.Empty(s.Audit.Entries);
        Assert.Equal(0, s.MachinePmRepo.AddManualCalls);
    }

    [Fact] // 10 + 11
    public async Task UnknownOrInactiveTargets_AreRejected()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => ScheduleMachine(s, MachineRequest(machineId: 999)));
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(machineId: 3)));
        Assert.Contains("The selected machine is not active.", inactive.Errors);
        await Assert.ThrowsAsync<NotFoundException>(() => ScheduleMold(s, MoldRequest(moldId: 999)));
        var retired = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMold(s, MoldRequest(moldId: 3)));
        Assert.Contains("The selected mold is retired.", retired.Errors);

        Assert.Empty(s.Data.Pms);
        Assert.Empty(s.MoldPmRepo.Pms);
    }

    [Fact] // 12
    public async Task MaintenanceType_FollowsThePlanRules()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => ScheduleMachine(s, MachineRequest(typeId: 999)));
        var wrongKind = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(typeId: 3))); // Mold-only + inactive
        Assert.Contains("The selected maintenance type does not apply to machines.", wrongKind.Errors);
        var invalid = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(typeId: 0)));
        Assert.Contains("MaintenanceTypeId is not valid.", invalid.Errors);

        var none = await ScheduleMachine(s, MachineRequest(typeId: null)); // optional, as on a plan
        Assert.Null(none.MaintenanceTypeId);
        var machineType = await ScheduleMachine(s, MachineRequest(typeId: 1));
        Assert.Equal(1, machineType.MaintenanceTypeId);
    }

    [Fact] // 13
    public async Task Checklist_FollowsTheChecklistMasterRules_AndIsOptional()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => ScheduleMachine(s, MachineRequest(masterId: 999)));
        var plan = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(masterId: 1))); // a PM plan
        Assert.Contains("The selected checklist is not a Checklist Master.", plan.Errors);
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(masterId: MaintenanceChecklistTestData.InactiveMasterId)));
        Assert.Contains("The selected Checklist Master is not active.", inactive.Errors);
        var empty = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(masterId: MaintenanceChecklistTestData.EmptyMasterId)));
        Assert.Contains("The selected Checklist Master has no checklist items.", empty.Errors);
        var moldMaster = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMachine(s, MachineRequest(masterId: MaintenanceChecklistTestData.MoldMasterId)));
        Assert.Contains("The selected Checklist Master does not apply to Machine.", moldMaster.Errors);
        var machineMaster = await Assert.ThrowsAsync<ValidationException>(() => ScheduleMold(s, MoldRequest(masterId: MaintenanceChecklistTestData.DefaultMasterId)));
        Assert.Contains("The selected Checklist Master does not apply to Mold.", machineMaster.Errors);
        Assert.Empty(s.Data.Pms);

        var withoutChecklist = await ScheduleMachine(s, MachineRequest(masterId: null));
        Assert.Empty(withoutChecklist.ChecklistItems);
    }

    [Fact] // past dates are allowed, as for a plan's start date - and the PM is then overdue
    public async Task APastDate_IsAccepted_AndOverdue()
    {
        var s = Create();
        var dto = await ScheduleMachine(s, MachineRequest(date: D(2026, 9, 1)));
        Assert.True(dto.IsOverdue);
    }

    // ================================================================ 14 + 15: notification and audit

    [Fact]
    public async Task ManualMachinePm_IsAudited_AndNotifiedOnce()
    {
        var s = Create();

        var dto = await ScheduleMachine(s, MachineRequest(typeId: 2));

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal((MachinePmAuditNames.Module, MachinePmAuditNames.ManualScheduled, MachinePmAuditNames.EntityName, dto.MachinePmId, dto.PmNo),
            (entry.Module, entry.Action, entry.EntityName, entry.EntityId!.Value, entry.RecordRef));
        Assert.Contains("manual (one-time) preventive maintenance", entry.Description);
        Assert.Contains("MAC-0001", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "scheduled_date", NewValue: "2026-10-15" });
        Assert.True(MachinePmAuditNames.ManualScheduled.Length <= 30 && MoldPmAuditNames.ManualScheduled.Length <= 30); // audit_log.action VARCHAR(30)

        var notification = Assert.Single(s.Notifications.Published);
        Assert.Equal(($"{NotificationTypes.MachinePmScheduled}:{dto.MachinePmId}", ModuleCodes.TrnMachinePm), (notification.EventKey, notification.ModuleCode));
    }

    [Fact]
    public async Task ManualMoldPm_IsAudited_WithoutANotification()
    {
        var s = Create();

        var dto = await ScheduleMold(s, MoldRequest());

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal((MoldPmAuditNames.ManualScheduled, dto.PmNo), (entry.Action, entry.RecordRef));
        Assert.Contains("MLD-0001", entry.Description);
        Assert.Empty(s.Notifications.Published); // mold PMs have no date-based notifications
    }

    [Fact] // a failed save writes nothing and audits nothing
    public async Task AFailedSave_WritesAndAuditsNothing()
    {
        var s = Create();
        s.MachinePmRepo.FailNextSave = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ScheduleMachine(s, MachineRequest()));

        Assert.Empty(s.Data.Pms);
        Assert.Empty(s.Audit.Entries);
        Assert.Empty(s.Notifications.Published);
    }

    // ================================================================ HTTP: contract, date, permission

    private (HttpClient Client, MachinePmScenario Data, InMemoryMoldPmRepository MoldPms) Http(Func<string, string, PermissionAction, bool>? decide = null)
    {
        var data = new MachinePmScenario();
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var molds = MoldTestData.Molds();
        var moldPms = new InMemoryMoldPmRepository(molds);
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMachinePmRepository>();
            services.AddSingleton<IMachinePmRepository>(new InMemoryMachinePmRepository(data));
            services.RemoveAll<IMoldPmRepository>();
            services.AddSingleton<IMoldPmRepository>(moldPms);
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(new InMemoryNotificationRepository());
            services.RemoveAll<IMachineRepository>();
            services.AddSingleton<IMachineRepository>(new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees));
            services.RemoveAll<IMoldRepository>();
            services.AddSingleton<IMoldRepository>(new InMemoryMoldRepository(molds, new InMemoryProductRepository(ProductTestData.Products()), employees));
            services.RemoveAll<IMaintenanceTypeRepository>();
            services.AddSingleton<IMaintenanceTypeRepository>(new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes()));
            services.RemoveAll<IMaintenanceChecklistRepository>();
            services.AddSingleton<IMaintenanceChecklistRepository>(new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.All()));
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new InMemoryUserRepository(UserTestData.Users(), roles.Find));
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
            services.RemoveAll<INotificationPublisher>();
            services.AddSingleton<INotificationPublisher>(new RecordingNotificationPublisher());
            services.RemoveAll<IPermissionAuthorizationService>();
            services.AddSingleton<IPermissionAuthorizationService>(decide is null ? new StubPermissionAuthorization(true) : new StubPermissionAuthorization(decide));
        }));
        var client = factory.CreateClient();
        TestAuth.Authenticate(client, factory, "ADMIN", 1);
        return (client, data, moldPms);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact] // 9 + 11 (date) + contract
    public async Task Http_ManualMachine_Is201_KeepsTheDateExactly_AndIgnoresAFrequency()
    {
        var (client, data, _) = Http();

        var response = await client.PostAsync("/api/v1/machine-maintenance/manual",
            Json("""{"title":"Spindle noise check","machineId":1,"maintenanceDate":"2026-10-15","maintenanceTypeId":2,"sourceChecklistId":901,"frequency":"Daily"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        var dto = root.GetProperty("data");
        Assert.Equal(("2026-10-15", "Manual", "Spindle noise check"), (dto.GetProperty("scheduledDate").GetString(), dto.GetProperty("scheduleType").GetString(), dto.GetProperty("title").GetString()));
        Assert.Equal(JsonValueKind.Null, dto.GetProperty("frequency").ValueKind);
        Assert.Equal(D(2026, 10, 15), Assert.Single(data.Pms).ScheduledDate);
    }

    [Fact]
    public async Task Http_ManualMold_Is201_AndMissingFieldsAre400()
    {
        var (client, _, moldPms) = Http();

        var ok = await client.PostAsync("/api/v1/mold-maintenance/manual", Json("""{"title":"Cavity polish","moldId":2,"maintenanceDate":"2026-10-15","sourceChecklistId":903}"""));
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        Assert.Equal(MoldPmCategory.Scheduled, Assert.Single(moldPms.Pms).Category);

        var bad = await client.PostAsync("/api/v1/machine-maintenance/manual", Json("""{"title":"x","frequency":"Daily"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = JsonDocument.Parse(await bad.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("MachineId is required.", errors);
        Assert.Contains("MaintenanceDate is required.", errors);
    }

    [Fact] // permission: TRN_*_PM Add - View alone is 403, and nothing is written
    public async Task Http_ManualScheduling_RequiresThePmAddPermission()
    {
        var (client, data, moldPms) = Http(decide: (_, module, action) => action == PermissionAction.View
            || (module == ModuleCodes.MasterMaintenanceChecklist && action == PermissionAction.Add));

        var machine = await client.PostAsync("/api/v1/machine-maintenance/manual", Json("""{"title":"x","machineId":1,"maintenanceDate":"2026-10-15"}"""));
        var mold = await client.PostAsync("/api/v1/mold-maintenance/manual", Json("""{"title":"x","moldId":1,"maintenanceDate":"2026-10-15"}"""));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (machine.StatusCode, mold.StatusCode));
        Assert.Empty(data.Pms);
        Assert.Empty(moldPms.Pms);
    }
}
