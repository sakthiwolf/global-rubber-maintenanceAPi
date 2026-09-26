using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Breakdown type endpoints (masters.breakdown_type_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MASTER_BREAKDOWN_TYPE permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/breakdown-types")]
public sealed class BreakdownTypeController : BaseApiController
{
    private readonly IBreakdownTypeService _breakdownTypeService;

    public BreakdownTypeController(IBreakdownTypeService breakdownTypeService)
    {
        _breakdownTypeService = breakdownTypeService;
    }

    /// <summary>Returns a paged list of breakdown types, optionally filtered by search text (code/name) and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterBreakdownType, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BreakdownTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<BreakdownTypeDto>>>> GetAll(
        [FromQuery] BreakdownTypeListQuery query, CancellationToken cancellationToken)
    {
        var result = await _breakdownTypeService.GetAllAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<BreakdownTypeDto>>.Ok(result, "Breakdown types retrieved successfully."));
    }

    /// <summary>Returns one breakdown type by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterBreakdownType, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BreakdownTypeDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var breakdownType = await _breakdownTypeService.GetByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<BreakdownTypeDto>.Ok(breakdownType, "Breakdown type retrieved successfully."));
    }

    /// <summary>
    /// Creates an active breakdown type; its code is issued from the BREAKDOWN_TYPE document sequence. 400 for an invalid field; 409 if
    /// another active breakdown type has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterBreakdownType, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BreakdownTypeDto>>> Create(
        [FromBody] CreateBreakdownTypeRequest request, CancellationToken cancellationToken)
    {
        var breakdownType = await _breakdownTypeService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = breakdownType.BreakdownTypeId },
            ApiResponse<BreakdownTypeDto>.Ok(breakdownType, "Breakdown type created successfully."));
    }

    /// <summary>
    /// Updates the name (the code is not editable). Requires the rowVersion from the last read: 409 if the breakdown type was
    /// modified since, or if another active breakdown type has the same name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterBreakdownType, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BreakdownTypeDto>>> Update(
        int id, [FromBody] UpdateBreakdownTypeRequest request, CancellationToken cancellationToken)
    {
        var breakdownType = await _breakdownTypeService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<BreakdownTypeDto>.Ok(breakdownType, "Breakdown type updated successfully."));
    }

    /// <summary>
    /// Deactivates a breakdown type - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is already
    /// inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterBreakdownType, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<BreakdownTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<BreakdownTypeDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var breakdownType = await _breakdownTypeService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<BreakdownTypeDto>.Ok(breakdownType, "Breakdown type deactivated successfully."));
    }
}

