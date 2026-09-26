using System.Text.Json.Serialization;
using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Body of POST /api/v1/machine-breakdowns.
/// BreakdownNo, Stage, and audit columns are server-controlled.
/// </summary>
public sealed class CreateMachineBreakdownRequest
{
    /// <summary>Required. Must match an active machine in masters.machine_master.</summary>
    public int? MachineId { get; init; }

    /// <summary>Required. The breakdown date (DATE, ISO yyyy-MM-dd).</summary>
    public DateOnly? BreakdownDate { get; init; }

    /// <summary>Required. The breakdown time (TIME, HH:mm or HH:mm:ss).</summary>
    [JsonConverter(typeof(NullableTimeOnlyJsonConverter))]
    public TimeOnly? BreakdownTime { get; init; }

    /// <summary>
    /// Optional free text. The person who reported the breakdown.
    /// Stored as-is (trimmed). NOT validated against Employee master.
    /// </summary>
    public string? ReportedBy { get; init; }

    /// <summary>Required. Short description of the problem (max 500 characters).</summary>
    public string? Problem { get; init; }

    /// <summary>Optional. Must match an active breakdown type in masters.breakdown_type_master.</summary>
    public int? BreakdownTypeId { get; init; }

    /// <summary>Optional. Low / Medium / High / Critical. Defaults to Medium.</summary>
    public string? Priority { get; init; }

    /// <summary>Optional. Additional details (max 1000 characters).</summary>
    public string? Description { get; init; }
}

/// <summary>
/// Query parameters for GET /api/v1/machine-breakdowns.
/// </summary>
public sealed class MachineBreakdownListQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;

    /// <summary>Searches BreakdownNo, MachineCode, MachineName, Problem, ReportedBy.</summary>
    public string? Search { get; init; }

    /// <summary>Optional stage filter: Reported / Assigned / Maintenance Started / Resolved / Closed.</summary>
    public string? Stage { get; init; }

    public int? MachineId { get; init; }
}

/// <summary>
/// Body of PUT /api/v1/machine-breakdowns/{id}/advance-stage.
/// Advances the breakdown stage by one step (forward only).
/// </summary>
public sealed class AdvanceMachineBreakdownStageRequest
{
    /// <summary>The new stage to move to. Must be the next valid stage.</summary>
    public string? Stage { get; init; }

    /// <summary>For the Assigned stage: the engineer being assigned.</summary>
    public int? AssignedEngineerId { get; init; }

    /// <summary>For the Resolved stage.</summary>
    public string? RootCause { get; init; }

    /// <summary>For the Resolved stage.</summary>
    public string? CorrectiveAction { get; init; }

    /// <summary>Optimistic concurrency token from the last read.</summary>
    public string? RowVersion { get; init; }
}
