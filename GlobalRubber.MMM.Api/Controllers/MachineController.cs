using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Machine endpoints (masters.machine_master): list, retrieve, create, update and deactivate. Every endpoint requires a
/// MasterMachine permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] - never a
/// role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/machines")]
public sealed class MachineController : BaseApiController
{
    private readonly IMachineService _machineService;

    public MachineController(IMachineService machineService)
    {
        _machineService = machineService;
    }

    /// <summary>
    /// Returns a paged list of machines (with department names), optionally filtered by search text
    /// (code/name), department, operational status and active status.
    /// </summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MachineDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<MachineDto>>>> GetAll(
        [FromQuery] MachineListQuery query, CancellationToken cancellationToken)
    {
        var result = await _machineService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<MachineDto>>.Ok(result, "Machines retrieved successfully."));
    }

    /// <summary>Returns one machine by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMachine, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<MachineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<MachineDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var machine = await _machineService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<MachineDto>.Ok(machine, "Machine retrieved successfully."));
    }

    /// <summary>
    /// Creates an active machine with operational status Running; its code is issued from the MACHINE document sequence.
    /// 400 for an invalid field or an inactive department; 404 for an unknown department; 409 if
    /// another active machine has the same name or the serial number is taken.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterMachine, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<MachineDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachineDto>>> Create(
        [FromBody] CreateMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = await _machineService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = machine.MachineId }, ApiResponse<MachineDto>.Ok(machine, "Machine created successfully."));
    }

    /// <summary>
    /// Updates the editable fields (the code, status, operational status and maintenance dates are not editable here).
    /// Requires the rowVersion from the last read: 409 if the machine was modified since, or for a duplicate name /
    /// serial number.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMachine, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<MachineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachineDto>>> Update(
        int id, [FromBody] UpdateMachineRequest request, CancellationToken cancellationToken)
    {
        var machine = await _machineService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MachineDto>.Ok(machine, "Machine updated successfully."));
    }

    /// <summary>
    /// Deactivates a machine - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is already
    /// inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterMachine, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<MachineDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<MachineDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var machine = await _machineService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<MachineDto>.Ok(machine, "Machine deactivated successfully."));
    }
}
