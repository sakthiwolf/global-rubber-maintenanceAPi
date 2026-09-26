using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Maintenance checklist endpoints (masters.maintenance_checklist_master + its items): list, retrieve, create, update and
/// deactivate. Every endpoint requires a MasterMaintenanceChecklist permission (View for reads, Add, Edit, Delete for
/// deactivation) via [RequirePermission] - never a role-name check. DELETE is a soft deactivation; nothing is ever
/// physically deleted.
/// </summary>
[Route("api/v1/maintenance-checklists")]
public sealed class MaintenanceChecklistController : BaseApiController
{
    private readonly IMaintenanceChecklistService _checklistService;

    public MaintenanceChecklistController(IMaintenanceChecklistService checklistService)
    {
        _checklistService = checklistService;
    }

    /// <summary>Returns a paged list of checklists (with items), optionally filtered by search text (code/name), applies-to and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MaintenanceChecklistDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MaintenanceChecklistDto>>>> GetAll(
        [FromQuery] MaintenanceChecklistListQuery query, CancellationToken cancellationToken)
    {
        var result = await _checklistService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MaintenanceChecklistDto>>.Ok(result, "Checklists retrieved successfully."));
    }

    /// <summary>
    /// Dropdown data for the checklist form: the ACTIVE machines a Machine checklist can be assigned to, in one call
    /// (approved C6). Served under the checklist's own View permission.
    /// </summary>
    [HttpGet("lookups")]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceChecklistLookupsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<MaintenanceChecklistLookupsDto>>> GetLookups(CancellationToken cancellationToken)
    {
        var lookups = await _checklistService.GetLookupsAsync(cancellationToken);

        return Ok(ApiResponse<MaintenanceChecklistLookupsDto>.Ok(lookups, "Checklist lookups retrieved successfully."));
    }

    /// <summary>Returns one checklist with its items by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceChecklistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MaintenanceChecklistDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var checklist = await _checklistService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MaintenanceChecklistDto>.Ok(checklist, "Checklist retrieved successfully."));
    }

    /// <summary>
    /// Creates an active checklist with its items; the code is issued from the MAINTENANCE_CHECKLIST document sequence.
    /// 400 for an invalid field, no non-blank item, a missing/invalid frequency, a missing machine (Machine), a machine on a
    /// Mold checklist, or an inactive machine; 404 for an unknown machine; 409 if another active checklist has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceChecklistDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceChecklistDto>>> Create(
        [FromBody] CreateMaintenanceChecklistRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklistService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = checklist.ChecklistId }, ApiResponse<MaintenanceChecklistDto>.Ok(checklist, "Checklist created successfully."));
    }

    /// <summary>
    /// Updates name, applies-to and the item list (replaced as a set; the code and status are not editable here). Requires
    /// the rowVersion from the last read: 409 if the checklist was modified since, or if another active checklist has the name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceChecklistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceChecklistDto>>> Update(
        int id, [FromBody] UpdateMaintenanceChecklistRequest request, CancellationToken cancellationToken)
    {
        var checklist = await _checklistService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MaintenanceChecklistDto>.Ok(checklist, "Checklist updated successfully."));
    }

    /// <summary>
    /// Deactivates a checklist - DELETE means SOFT deactivation (IsActive = false); the header and items are kept. 409 if
    /// it is already inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMaintenanceChecklist, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<MaintenanceChecklistDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MaintenanceChecklistDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var checklist = await _checklistService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MaintenanceChecklistDto>.Ok(checklist, "Checklist deactivated successfully."));
    }
}
