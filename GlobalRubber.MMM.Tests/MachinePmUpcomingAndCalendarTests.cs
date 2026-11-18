using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The Upcoming tab (open PMs scheduled after today) and the F05-style year calendar (real jobs + projected occurrences).
/// Machines: 1 MAC-0001, 2 MAC-0002 (MachineTestData).
/// </summary>
public class MachinePmUpcomingAndCalendarTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static MachinePmService Service(MachinePmScenario data, DateOnly today)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        return new MachinePmService(new InMemoryMachinePmRepository(data), users, new SettableClock(today), new RecordingAuditLog(),
            new RecordingNotificationPublisher(), NullLogger<MachinePmService>.Instance);
    }

    // ================================================================ Upcoming tab

    [Fact]
    public async Task Upcoming_ListsOpenPmsAfterToday_PlanAndOneTime_SoonestFirst_AndIsCounted()
    {
        var data = new MachinePmScenario();
        var weekly = data.Checklist("Weekly", D(2026, 11, 23));
        var monthly = data.Checklist("Monthly", D(2026, 12, 1));
        var dueToday = data.Open(data.Checklist("Daily", D(2026, 11, 18)), D(2026, 11, 18));
        var nextWeek = data.Open(weekly, D(2026, 11, 23));
        var nextMonth = data.Open(monthly, D(2026, 12, 1));
        var oneTime = data.Open(null, D(2026, 11, 25), machineId: 2);
        oneTime.ScheduleType = PmScheduleType.Manual;
        oneTime.Title = "Motor alignment";
        data.Completed(weekly, D(2026, 11, 30)); // completed: never upcoming, even with a future date
        var service = Service(data, D(2026, 11, 18));

        var page = await service.GetAllAsync(new MachinePmListQuery { Bucket = MachinePmBucket.Upcoming, PageNumber = 1, PageSize = 10 }, CancellationToken.None);
        var counts = await service.GetBucketCountsAsync(new MachinePmListQuery(), CancellationToken.None);

        Assert.Equal(new[] { nextWeek.PmNo, oneTime.PmNo, nextMonth.PmNo }, page.Items.Select(i => i.PmNo));
        Assert.DoesNotContain(page.Items, i => i.PmNo == dueToday.PmNo); // due today belongs to its frequency tab
        Assert.Equal(3, counts.Upcoming);
        Assert.Equal(1, counts.Daily);
    }

    [Fact]
    public async Task Upcoming_IsAValidBucket()
    {
        var page = await Service(new MachinePmScenario(), D(2026, 11, 18))
            .GetAllAsync(new MachinePmListQuery { Bucket = "upcoming", PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Empty(page.Items);
    }

    // ================================================================ calendar

    [Fact]
    public async Task Calendar_ProjectsEveryWeeklyAndMonthlyDate_FromTheOpenJob_ToTheYearEnd()
    {
        var data = new MachinePmScenario();
        var weekly = data.Checklist("Weekly", D(2026, 11, 23));
        var monthly = data.Checklist("Monthly", D(2026, 12, 1));
        var weeklyJob = data.Open(weekly, D(2026, 11, 23));
        data.Open(monthly, D(2026, 12, 1));

        var cal = await Service(data, D(2026, 11, 18)).GetCalendarAsync(2026, CancellationToken.None);

        var row = Assert.Single(cal.Machines);
        Assert.Equal(("MAC-0001", 2), (row.MachineCode, row.Plans.Count));
        var weeklyDates = row.Entries.Where(e => e.Frequency == "Weekly").Select(e => (e.Date, e.Status)).ToList();
        Assert.Equal(new[]
        {
            (D(2026, 11, 23), MachinePmCalendarStatus.Scheduled), (D(2026, 11, 30), MachinePmCalendarStatus.Planned),
            (D(2026, 12, 7), MachinePmCalendarStatus.Planned), (D(2026, 12, 14), MachinePmCalendarStatus.Planned),
            (D(2026, 12, 21), MachinePmCalendarStatus.Planned), (D(2026, 12, 28), MachinePmCalendarStatus.Planned),
        }, weeklyDates);
        Assert.Equal(weeklyJob.PmNo, row.Entries.First().PmNo);
        Assert.Equal(new[] { D(2026, 12, 1) }, row.Entries.Where(e => e.Frequency == "Monthly").Select(e => e.Date));
        Assert.Equal(row.Entries.OrderBy(e => e.Date).Select(e => e.Date), row.Entries.Select(e => e.Date));
    }

    [Fact]
    public async Task Calendar_NextYear_ShowsTheYearlyPmAndTwelveMonthlies()
    {
        var data = new MachinePmScenario();
        data.Open(data.Checklist("Monthly", D(2026, 12, 1)), D(2026, 12, 1));
        data.Open(data.Checklist("Yearly", D(2027, 4, 1)), D(2027, 4, 1));

        var cal = await Service(data, D(2026, 11, 18)).GetCalendarAsync(2027, CancellationToken.None);

        var row = Assert.Single(cal.Machines);
        Assert.Equal(12, row.Entries.Count(e => e.Frequency == "Monthly" && e.Status == MachinePmCalendarStatus.Planned));
        var yearly = Assert.Single(row.Entries, e => e.Frequency == "Yearly");
        Assert.Equal((D(2027, 4, 1), MachinePmCalendarStatus.Scheduled), (yearly.Date, yearly.Status)); // the real job
    }

    [Fact]
    public async Task Calendar_AnOverdueJob_IsShownOverdue_AndMissedCyclesAreNotProjected()
    {
        var data = new MachinePmScenario();
        var weekly = data.Checklist("Weekly", D(2026, 10, 5));
        data.Completed(weekly, D(2026, 10, 5));
        data.Open(weekly, D(2026, 10, 12)); // 5 weeks overdue on 18-Nov

        var cal = await Service(data, D(2026, 11, 18)).GetCalendarAsync(2026, CancellationToken.None);

        var entries = Assert.Single(cal.Machines).Entries;
        Assert.Equal(MachinePmCalendarStatus.Completed, entries[0].Status);
        Assert.Equal((D(2026, 10, 12), MachinePmCalendarStatus.Overdue), (entries[1].Date, entries[1].Status));
        // Completing late re-anchors on the plan's cycle after today: the next Monday-cycle date is 23-Nov, not 19-Oct.
        Assert.Equal(D(2026, 11, 23), entries[2].Date);
        Assert.DoesNotContain(entries, e => e.Status == MachinePmCalendarStatus.Planned && e.Date < D(2026, 11, 18));
    }

    [Fact]
    public async Task Calendar_OneTimePm_IsShownWithItsTitle_AndAPlanWithoutAnOpenJobProjectsNothing()
    {
        var data = new MachinePmScenario();
        data.Checklist("Monthly", D(2026, 12, 1), machineId: 2); // no open job
        var manual = data.Open(null, D(2026, 12, 10), machineId: 1);
        manual.ScheduleType = PmScheduleType.Manual;
        manual.Title = "Motor alignment";

        var cal = await Service(data, D(2026, 11, 18)).GetCalendarAsync(2026, CancellationToken.None);

        var m1 = Assert.Single(cal.Machines, m => m.MachineId == 1);
        var entry = Assert.Single(m1.Entries);
        Assert.Equal(("Motor alignment", (string?)null, MachinePmCalendarStatus.Scheduled), (entry.PlanName, entry.Frequency, entry.Status));
        Assert.Empty(Assert.Single(cal.Machines, m => m.MachineId == 2).Entries);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task Calendar_YearOutOfRange_Is400(int year)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service(new MachinePmScenario(), D(2026, 11, 18)).GetCalendarAsync(year, CancellationToken.None));
    }

    [Fact]
    public async Task Calendar_DefaultsToThePlantYear()
    {
        var cal = await Service(new MachinePmScenario(), D(2026, 11, 18)).GetCalendarAsync(null, CancellationToken.None);

        Assert.Equal((2026, D(2026, 11, 18)), (cal.Year, cal.Today));
    }
}
