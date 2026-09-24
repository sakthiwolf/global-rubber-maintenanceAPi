using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Maintenance type endpoints (masters.maintenance_type_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MasterMaintenanceType permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/maintenance-types")]
public sealed class MaintenanceTypeController : BaseApiController
{
    private readonly IMaintenanceTypeService _maintenanceTypeService;

    public MaintenanceTypeController(IMaintenanceTypeService maintenanceTypeService)
    {
        _maintenanceTypeService = maintenanceTypeService;
    }

    /// <summary>Returns a paged list of maintenance types, optionally filtered by search text (code/name), applies-to and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterMaintenanceType, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MaintenanceTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MaintenanceTypeDto>>>> GetAll(
        [FromQuery] MaintenanceTypeListQuery query, CancellationToken cancellationToken)
    {
        var result = await _maintenanceTypeService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MaintenanceTypeDto>>.Ok(result, "Maintenance types retrieved successfully."));
    }

    /// <summary>Returns one maintenance type by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceType, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MaintenanceTypeDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var maintenanceType = await _maintenanceTypeService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MaintenanceTypeDto>.Ok(maintenanceType, "Maintenance type retrieved successfully."));
    }

    /// <summary>
    /// Creates an active maintenance type; its code is issued from the MAINTENANCE_TYPE document sequence. 400 for an invalid field; 409 if
    /// another active maintenance type has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterMaintenanceType, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceTypeDto>>> Create(
        [FromBody] CreateMaintenanceTypeRequest request, CancellationToken cancellationToken)
    {
        var maintenanceType = await _maintenanceTypeService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = maintenanceType.MaintenanceTypeId }, ApiResponse<MaintenanceTypeDto>.Ok(maintenanceType, "Maintenance type created successfully."));
    }

    /// <summary>
    /// Updates name and applies-to (the code and status are not editable here). Requires the rowVersion from the last
    /// read: 409 if the maintenance type was modified since, or if another active maintenance type has the name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceType, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceTypeDto>>> Update(
        int id, [FromBody] UpdateMaintenanceTypeRequest request, CancellationToken cancellationToken)
    {
        var maintenanceType = await _maintenanceTypeService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MaintenanceTypeDto>.Ok(maintenanceType, "Maintenance type updated successfully."));
    }

    /// <summary>
    /// Deactivates a maintenance type - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is already
    /// inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceType, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceTypeDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var maintenanceType = await _maintenanceTypeService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MaintenanceTypeDto>.Ok(maintenanceType, "Maintenance type deactivated successfully."));
    }
}
