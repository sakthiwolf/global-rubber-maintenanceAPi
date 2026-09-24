using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Mold endpoints (masters.mold_master): list, retrieve, create, update and retire. Every endpoint requires a MasterMold
/// permission (View for reads, Add, Edit, Delete for retiring) via [RequirePermission] - never a role-name check. DELETE
/// sets status Retired; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/molds")]
public sealed class MoldController : BaseApiController
{
    private readonly IMoldService _moldService;

    public MoldController(IMoldService moldService)
    {
        _moldService = moldService;
    }

    /// <summary>
    /// Returns a paged list of molds (with product and responsible-person names and the derived life values), optionally
    /// filtered by search text (code/name), status, product and life state.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MoldDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MoldDto>>>> GetAll(
        [FromQuery] MoldListQuery query, CancellationToken cancellationToken)
    {
        var result = await _moldService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MoldDto>>.Ok(result, "Molds retrieved successfully."));
    }

    /// <summary>Returns one mold by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMold, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MoldDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MoldDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var mold = await _moldService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MoldDto>.Ok(mold, "Mold retrieved successfully."));
    }

    /// <summary>
    /// Creates a mold with 0 usage; its code is issued from the MOLD document sequence. 400 for an invalid field or an
    /// inactive product / person; 404 for an unknown product / person; 409 if another non-retired mold has the same name
    /// or the serial number is taken.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterMold, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<MoldDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MoldDto>>> Create(
        [FromBody] CreateMoldRequest request, CancellationToken cancellationToken)
    {
        var mold = await _moldService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = mold.MoldId }, ApiResponse<MoldDto>.Ok(mold, "Mold created successfully."));
    }

    /// <summary>
    /// Updates the editable fields, including status and a usage correction (the code is not editable). Requires the
    /// rowVersion from the last read: 409 if the mold was modified since, or for a duplicate name / serial number.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMold, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MoldDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MoldDto>>> Update(
        int id, [FromBody] UpdateMoldRequest request, CancellationToken cancellationToken)
    {
        var mold = await _moldService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MoldDto>.Ok(mold, "Mold updated successfully."));
    }

    /// <summary>
    /// "Deactivates" a mold the way the mold table models it - status Retired (there is no is_active column); the row is
    /// kept. 409 if it is already retired.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMold, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<MoldDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MoldDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var mold = await _moldService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MoldDto>.Ok(mold, "Mold retired successfully."));
    }
}
