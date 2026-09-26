using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Machine Breakdown endpoints (transactions.machine_breakdown_transaction):
///   GET  /api/v1/machine-breakdowns         - paged list, search, stage filter
///   GET  /api/v1/machine-breakdowns/{id}    - one breakdown
///   POST /api/v1/machine-breakdowns         - report a new breakdown (stage = Reported)
///   PUT  /api/v1/machine-breakdowns/{id}/advance-stage  - advance to the next stage
///
/// All reads require View, creates require Add, stage advances require Edit.
/// No role-name checks; permissions are evaluated by [RequirePermission].
/// </summary>
[Route("api/v1/machine-breakdowns")]
public sealed class MachineBreakdownController : BaseApiController
{
    private readonly IMachineBreakdownService _service;

    public MachineBreakdownController(IMachineBreakdownService service)
    {
        _service = service;
    }

    /// <summary>
    /// A page of breakdowns, newest first. Supports search (BreakdownNo / MachineCode /
    /// MachineName / Problem / ReportedBy) and stage filter.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.TrnMachineBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineBreakdownDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineBreakdownDto>>>> GetAll(
        [FromQuery] MachineBreakdownListQuery query, CancellationToken cancellationToken)
    {
        var result = await _service.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MachineBreakdownDto>>.Ok(result, "Machine breakdowns retrieved successfully."));
    }

    /// <summary>One breakdown with all details. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.TrnMachineBreakdown, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachineBreakdownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachineBreakdownDto>>> GetById(
        int id, CancellationToken cancellationToken)
    {
        var breakdown = await _service.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MachineBreakdownDto>.Ok(breakdown, "Machine breakdown retrieved successfully."));
    }

    /// <summary>
    /// Report a new machine breakdown. Machine must be active.
    /// ReportedBy is free text (stored as typed; NOT an employee lookup).
    /// Returns 201 Created with the generated BRK-NNNN breakdown number.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.TrnMachineBreakdown, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<MachineBreakdownDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachineBreakdownDto>>> Create(
        [FromBody] CreateMachineBreakdownRequest request, CancellationToken cancellationToken)
    {
        var created = await _service.CreateAsync(
            request, GetAuthenticatedUserId(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.MachineBreakdownId },
            ApiResponse<MachineBreakdownDto>.Ok(created, "Breakdown reported successfully."));
    }

    /// <summary>
    /// Advances the breakdown stage by exactly one step. Requires the rowVersion from the last read
    /// to protect against concurrent edits.
    /// </summary>
    [HttpPut("{id:int}/advance-stage")]
    [RequirePermission(ModuleCodes.TrnMachineBreakdown, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MachineBreakdownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachineBreakdownDto>>> AdvanceStage(
        int id, [FromBody] AdvanceMachineBreakdownStageRequest request, CancellationToken cancellationToken)
    {
        var updated = await _service.AdvanceStageAsync(
            id, request, GetAuthenticatedUserId(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return Ok(ApiResponse<MachineBreakdownDto>.Ok(updated, $"Breakdown moved to \"{updated.Stage}\"."));
    }
}
