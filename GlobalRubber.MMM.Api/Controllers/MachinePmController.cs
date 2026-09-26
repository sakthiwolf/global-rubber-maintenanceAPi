using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Machine preventive-maintenance endpoints (transactions.machine_pm_transaction + its checklist snapshot): bucketed list,
/// tab counts, details, lookups and complete. Occurrences are created by the Maintenance Checklist (first occurrence) and
/// by completion (the successor) - there is no manual scheduling endpoint (removed with the recurring workflow,
/// 2026-09-25). Every endpoint requires a TrnMachinePm permission via [RequirePermission] - never a role-name check: View
/// for reads, Edit to complete (PUT because every POST in the API is an Add). No start, edit, cancel or delete endpoint.
/// </summary>
[Route("api/v1/machine-maintenance")]
public sealed class MachinePmController : BaseApiController
{
    private readonly IMachinePmService _machinePmService;

    public MachinePmController(IMachinePmService machinePmService)
    {
        _machinePmService = machinePmService;
    }

    /// <summary>
    /// A page of PMs, optionally one tab (daily/weekly/monthly/yearly = DUE open PMs of that checklist frequency - scheduled
    /// on or before today's IST date, overdue included; completed = completed PMs), one machine, or a search on PM number /
    /// machine. Without a tab every PM is returned, including future occurrences.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.TrnMachinePm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachinePmDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachinePmDto>>>> GetAll(
        [FromQuery] MachinePmListQuery query, CancellationToken cancellationToken)
    {
        var result = await _machinePmService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MachinePmDto>>.Ok(result, "Machine maintenance records retrieved successfully."));
    }

    /// <summary>How many PMs are in each tab (daily, weekly, monthly, yearly, completed), under the same machine/search filters.</summary>
    [HttpGet("counts")]
    [RequirePermission(ModuleCodes.TrnMachinePm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachinePmBucketCountsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MachinePmBucketCountsDto>>> GetCounts(
        [FromQuery] MachinePmListQuery query, CancellationToken cancellationToken)
    {
        var counts = await _machinePmService.GetBucketCountsAsync(query, cancellationToken);

        return Ok(ApiResponse<MachinePmBucketCountsDto>.Ok(counts, "Machine maintenance counts retrieved successfully."));
    }

    /// <summary>Dropdown data for the page: the active machines (machine filter).</summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.TrnMachinePm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachinePmLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MachinePmLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _machinePmService.GetLookupsAsync(cancellationToken);

        return Ok(ApiResponse<MachinePmLookupsDto>.Ok(lookups, "Machine maintenance lookups retrieved successfully."));
    }

    /// <summary>One PM with its checklist snapshot and the rowVersion needed to complete it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.TrnMachinePm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachinePmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachinePmDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var pm = await _machinePmService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MachinePmDto>.Ok(pm, "Machine maintenance record retrieved successfully."));
    }

    /// <summary>
    /// Completes a PM: Completed on today's plant date, Maintenance By (required), checklist ticks and remarks saved; in the
    /// same transaction the next occurrence is created when the checklist is still an active Machine checklist, and the
    /// machine's last/next maintenance dates are updated. Requires the rowVersion from the last read: 409 if it changed
    /// since, or if the PM is already completed.
    /// </summary>
    [HttpPut("{id:int}/complete")]
    [RequirePermission(ModuleCodes.TrnMachinePm, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MachinePmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachinePmDto>>> Complete(
        int id, [FromBody] CompleteMachinePmRequest request, CancellationToken cancellationToken)
    {
        var pm = await _machinePmService.CompleteAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MachinePmDto>.Ok(pm, "Maintenance completed successfully."));
    }
}
