using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Spare part endpoints (masters.spare_part_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MasterSparePart permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/spare-parts")]
public sealed class SparePartController : BaseApiController
{
    private readonly ISparePartService _sparePartService;

    public SparePartController(ISparePartService sparePartService)
    {
        _sparePartService = sparePartService;
    }

    /// <summary>
    /// Returns a paged list of spare parts, optionally filtered by search text (code/name), stock status, linked machine and
    /// active status.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SparePartDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<SparePartDto>>>> GetAll(
        [FromQuery] SparePartListQuery query, CancellationToken cancellationToken)
    {
        var result = await _sparePartService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<SparePartDto>>.Ok(result, "Spare parts retrieved successfully."));
    }

    /// <summary>Returns one spare part by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterSparePart, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<SparePartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SparePartDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<SparePartDto>.Ok(sparePart, "Spare part retrieved successfully."));
    }

    /// <summary>
    /// Creates an active spare part; its code is issued from the SPARE_PART document sequence and its stock status is
    /// computed by the database. 400 for an invalid field or an inactive machine / supplier; 404 if the machine / supplier
    /// does not exist; 409 if another active spare part has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterSparePart, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<SparePartDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SparePartDto>>> Create(
        [FromBody] CreateSparePartRequest request, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = sparePart.SparePartId }, ApiResponse<SparePartDto>.Ok(sparePart, "Spare part created successfully."));
    }

    /// <summary>
    /// Updates the editable fields, including current stock (edited directly in the master - Q-06). The code, status and
    /// stock status are not editable here. Requires the rowVersion from the last read: 409 if the spare part was modified
    /// since (for example by a usage transaction), or if another active spare part has the name.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterSparePart, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<SparePartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SparePartDto>>> Update(
        int id, [FromBody] UpdateSparePartRequest request, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<SparePartDto>.Ok(sparePart, "Spare part updated successfully."));
    }

    /// <summary>
    /// Deactivates a spare part - DELETE means SOFT deactivation (IsActive = false); the row and its stock are kept. 409 if it
    /// is already inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterSparePart, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<SparePartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SparePartDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<SparePartDto>.Ok(sparePart, "Spare part deactivated successfully."));
    }
}
