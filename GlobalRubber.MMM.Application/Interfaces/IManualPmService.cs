using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Manual / One-Time preventive maintenance (migration 025): schedules exactly ONE PM for a specific machine or mold on a
/// specific date. The automatic flows (recurring plan occurrences, usage-based mold PM) are untouched.
/// </summary>
public interface IManualPmService
{
    Task<MachinePmDto> ScheduleMachineAsync(ScheduleManualMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    Task<MoldPmDto> ScheduleMoldAsync(ScheduleManualMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
