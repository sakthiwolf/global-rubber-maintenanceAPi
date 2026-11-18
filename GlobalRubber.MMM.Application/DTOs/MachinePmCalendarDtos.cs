namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// GET /api/v1/machine-maintenance/calendar?year= - the year's machine PM plan in the layout of the plant's F05
/// "Preventive Maintenance Schedule": one row per machine, every PM date of the year. Real PM jobs keep their own status;
/// later occurrences that do not exist yet (the system creates the next job only when the previous one is completed) are
/// projected from each plan's cycle and shown as "Planned". Read-only: nothing is created.
/// </summary>
public sealed class MachinePmCalendarDto
{
    public int Year { get; init; }

    /// <summary>The plant (IST) date the statuses were worked out on.</summary>
    public DateOnly Today { get; init; }

    public IReadOnlyList<MachinePmCalendarMachineDto> Machines { get; init; } = Array.Empty<MachinePmCalendarMachineDto>();
}

public sealed class MachinePmCalendarMachineDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;

    /// <summary>The machine's active PM plans (name + frequency).</summary>
    public IReadOnlyList<MachinePmCalendarPlanDto> Plans { get; init; } = Array.Empty<MachinePmCalendarPlanDto>();

    /// <summary>Every PM date of the year, in date order.</summary>
    public IReadOnlyList<MachinePmCalendarEntryDto> Entries { get; init; } = Array.Empty<MachinePmCalendarEntryDto>();
}

public sealed class MachinePmCalendarPlanDto
{
    public int ChecklistId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Frequency { get; init; } = string.Empty;
}

public sealed class MachinePmCalendarEntryDto
{
    /// <summary>The day the PM is (or was) due.</summary>
    public DateOnly Date { get; init; }

    /// <summary>Completed / In Progress / Overdue / Due / Scheduled (a real job) or Planned (projected, no job yet).</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>The plan's frequency; null for a one-time (manual) PM.</summary>
    public string? Frequency { get; init; }

    /// <summary>The plan name, or the title of a one-time PM.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>The real job, when there is one (null for Planned).</summary>
    public int? MachinePmId { get; init; }
    public string? PmNo { get; init; }
    public DateOnly? CompletedDate { get; init; }
}

/// <summary>What the repository reads for the calendar: an active machine plan.</summary>
public sealed record MachinePmCalendarPlanSource(
    int ChecklistId, string Name, string Frequency, int MachineId, string MachineCode, string MachineName, DateOnly CycleAnchor);

/// <summary>What the repository reads for the calendar: a machine PM job (of the year, or still open).</summary>
public sealed record MachinePmCalendarJobSource(
    int MachinePmId, string PmNo, int MachineId, string MachineCode, string MachineName, int? ChecklistId, string? PlanName,
    string? Frequency, string ScheduleType, string? Title, DateOnly ScheduledDate, DateOnly? CompletedDate, string Status);
