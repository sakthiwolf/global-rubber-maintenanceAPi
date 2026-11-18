using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Rules;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>Status labels of a calendar entry.</summary>
public static class MachinePmCalendarStatus
{
    public const string Completed = "Completed";
    public const string InProgress = "In Progress";
    public const string Overdue = "Overdue";
    public const string Due = "Due";
    public const string Scheduled = "Scheduled";

    /// <summary>Projected from the plan's cycle - no PM job exists for it yet.</summary>
    public const string Planned = "Planned";
}

/// <summary>
/// Builds the F05-style year calendar (pure - no I/O). Real jobs scheduled in the year keep their own status. For each
/// active plan, the occurrences AFTER its open job are projected with the same rule the system uses to create the next
/// job (RecurrenceRules, from the plan's cycle anchor): from the open job's date, or from today when that job is overdue
/// - completing late never schedules the missed cycles, so they are not shown either.
/// </summary>
public static class MachinePmCalendarBuilder
{
    private const int MaxProjectedPerPlan = 400; // a Daily plan has at most 366 dates in a year

    public static MachinePmCalendarDto Build(
        int year, DateOnly today, IReadOnlyList<MachinePmCalendarPlanSource> plans, IReadOnlyList<MachinePmCalendarJobSource> jobs)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var rows = new Dictionary<int, (string Code, string Name, List<MachinePmCalendarPlanDto> Plans, List<MachinePmCalendarEntryDto> Entries)>();

        (string Code, string Name, List<MachinePmCalendarPlanDto> Plans, List<MachinePmCalendarEntryDto> Entries) RowFor(int machineId, string code, string name)
        {
            if (!rows.TryGetValue(machineId, out var row))
            {
                row = (code, name, new List<MachinePmCalendarPlanDto>(), new List<MachinePmCalendarEntryDto>());
                rows[machineId] = row;
            }

            return row;
        }

        foreach (var job in jobs.Where(j => j.ScheduledDate >= from && j.ScheduledDate <= to))
        {
            RowFor(job.MachineId, job.MachineCode, job.MachineName).Entries.Add(new MachinePmCalendarEntryDto
            {
                Date = job.ScheduledDate,
                Status = StatusOf(job, today),
                Frequency = job.ScheduleType == PmScheduleType.Manual ? null : job.Frequency,
                PlanName = job.ScheduleType == PmScheduleType.Manual ? job.Title ?? "One-time maintenance" : job.PlanName ?? "Preventive maintenance",
                MachinePmId = job.MachinePmId,
                PmNo = job.PmNo,
                CompletedDate = job.CompletedDate,
            });
        }

        foreach (var plan in plans)
        {
            var row = RowFor(plan.MachineId, plan.MachineCode, plan.MachineName);
            row.Plans.Add(new MachinePmCalendarPlanDto { ChecklistId = plan.ChecklistId, Name = plan.Name, Frequency = plan.Frequency });

            var open = jobs
                .Where(j => j.ChecklistId == plan.ChecklistId && j.Status != MachinePmStatus.Completed)
                .OrderBy(j => j.ScheduledDate)
                .FirstOrDefault();
            if (open is null)
            {
                continue; // no open occurrence: the plan creates nothing more until one exists
            }

            var current = open.ScheduledDate >= today ? open.ScheduledDate : today;
            for (var i = 0; i < MaxProjectedPerPlan; i++)
            {
                var next = RecurrenceRules.FirstAfter(plan.CycleAnchor, plan.Frequency, current);
                if (next > to) break;
                if (next >= from)
                {
                    row.Entries.Add(new MachinePmCalendarEntryDto
                    {
                        Date = next, Status = MachinePmCalendarStatus.Planned, Frequency = plan.Frequency, PlanName = plan.Name,
                    });
                }

                current = next;
            }
        }

        return new MachinePmCalendarDto
        {
            Year = year,
            Today = today,
            Machines = rows
                .OrderBy(r => r.Value.Code, StringComparer.OrdinalIgnoreCase)
                .Select(r => new MachinePmCalendarMachineDto
                {
                    MachineId = r.Key,
                    MachineCode = r.Value.Code,
                    MachineName = r.Value.Name,
                    Plans = r.Value.Plans.OrderBy(p => FrequencyOrder(p.Frequency)).ThenBy(p => p.Name).ToList(),
                    Entries = r.Value.Entries.OrderBy(e => e.Date).ThenBy(e => FrequencyOrder(e.Frequency)).ToList(),
                })
                .ToList(),
        };
    }

    private static string StatusOf(MachinePmCalendarJobSource job, DateOnly today) => job.Status switch
    {
        MachinePmStatus.Completed => MachinePmCalendarStatus.Completed,
        MachinePmStatus.InProgress => MachinePmCalendarStatus.InProgress,
        _ when job.ScheduledDate < today => MachinePmCalendarStatus.Overdue,
        _ when job.ScheduledDate == today => MachinePmCalendarStatus.Due,
        _ => MachinePmCalendarStatus.Scheduled,
    };

    private static int FrequencyOrder(string? frequency) => frequency switch
    {
        ChecklistFrequency.Daily => 0,
        ChecklistFrequency.Weekly => 1,
        ChecklistFrequency.Monthly => 2,
        ChecklistFrequency.Yearly => 3,
        _ => 4,
    };
}
