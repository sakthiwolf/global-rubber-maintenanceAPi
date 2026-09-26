using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Mold preventive-maintenance endpoints (transactions.mold_pm_transaction). Mold PMs are created AUTOMATICALLY from the
/// mold's usage (Production Entry, Mold update, completion - IMoldPmEvaluator), so there is deliberately no POST /
/// scheduling endpoint. Every endpoint requires a TrnMoldPm permission via [RequirePermission] - never a role-name check:
/// View for reads, Edit to start / complete (PUT because every POST in the API is an Add).
/// </summary>
[Route("api/v1/mold-maintenance")]
public sealed class MoldPmController : BaseApiController
{
    private readonly IMoldPmService _moldPmService;

    public MoldPmController(IMoldPmService moldPmService)
    {
        _moldPmService = moldPmService;
    }

    /// <summary>
    /// A page of Mold PMs, optionally one tab (due / overdue / in-progress / completed - due and overdue compare the day the
    /// PM became due with today's IST date), one mold, or a search on PM number / mold.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MoldPmDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MoldPmDto>>>> GetAll([FromQuery] MoldPmListQuery query, CancellationToken cancellationToken)
    {
        var result = await _moldPmService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MoldPmDto>>.Ok(result, "Mold maintenance records retrieved successfully."));
    }

    /// <summary>How many PMs are in each tab (due, overdue, in-progress, completed), under the same mold/search filters.</summary>
    [HttpGet("counts")]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldPmCountsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MoldPmCountsDto>>> GetCounts([FromQuery] MoldPmListQuery query, CancellationToken cancellationToken)
    {
        var counts = await _moldPmService.GetCountsAsync(query, cancellationToken);

        return Ok(ApiResponse<MoldPmCountsDto>.Ok(counts, "Mold maintenance counts retrieved successfully."));
    }

    /// <summary>
    /// Every mold's usage-based PM position: cumulative shots, interval, next threshold, remaining shots, warning margin,
    /// last maintenance and the derived state (Not Configured / Normal / Warning / Due / Overdue / In Maintenance).
    /// </summary>
    [HttpGet("mold-usage")]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MoldUsageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MoldUsageDto>>>> GetMoldUsage(CancellationToken cancellationToken)
    {
        var usage = await _moldPmService.GetMoldUsageAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<MoldUsageDto>>.Ok(usage, "Mold usage retrieved successfully."));
    }

    /// <summary>One PM with the rowVersion needed to start / complete it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldPmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MoldPmDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var pm = await _moldPmService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MoldPmDto>.Ok(pm, "Mold maintenance record retrieved successfully."));
    }

    /// <summary>Starts a Scheduled PM (In Progress; the mold goes to Maintenance). 409 if stale or not Scheduled.</summary>
    [HttpPut("{id:int}/start")]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MoldPmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MoldPmDto>>> Start(int id, [FromBody] StartMoldPmRequest request, CancellationToken cancellationToken)
    {
        var pm = await _moldPmService.StartAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MoldPmDto>.Ok(pm, "Maintenance started successfully."));
    }

    /// <summary>
    /// Completes a PM on today's plant date with Maintenance By (required) and remarks; in the same transaction the usage at
    /// completion is recorded and the mold's next cycle is anchored on the PM's threshold. 409 if stale or already completed.
    /// </summary>
    [HttpPut("{id:int}/complete")]
    [RequirePermission(ModuleCodes.TrnMoldPm, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MoldPmDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MoldPmDto>>> Complete(int id, [FromBody] CompleteMoldPmRequest request, CancellationToken cancellationToken)
    {
        var pm = await _moldPmService.CompleteAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MoldPmDto>.Ok(pm, "Maintenance completed successfully."));
    }
}
