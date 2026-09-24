using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Department endpoints (masters.department_master): list, retrieve, create, update and deactivate. Every
/// endpoint requires a MasterDepartment permission (View for reads, Add, Edit, Delete for deactivation) via
/// [RequirePermission] - never a role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/departments")]
public sealed class DepartmentController : BaseApiController
{
    private readonly IDepartmentService _departmentService;

    public DepartmentController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    /// <summary>Returns a paged list of departments, optionally filtered by search text (code/name) and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterDepartment, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DepartmentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<DepartmentDto>>>> GetAll(
        [FromQuery] DepartmentListQuery query, CancellationToken cancellationToken)
    {
        var result = await _departmentService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<DepartmentDto>>.Ok(result, "Departments retrieved successfully."));
    }

    /// <summary>Returns one department by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterDepartment, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var department = await _departmentService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<DepartmentDto>.Ok(department, "Department retrieved successfully."));
    }

    /// <summary>
    /// Creates an active department; its code is issued from the DEPARTMENT document sequence. 400 for an
    /// invalid field; 409 if another active department has the same name.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterDepartment, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> Create(
        [FromBody] CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await _departmentService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = department.DepartmentId }, ApiResponse<DepartmentDto>.Ok(department, "Department created successfully."));
    }

    /// <summary>
    /// Updates name and remarks (the code and status are not editable here). Requires the rowVersion from the
    /// last read: 409 if the department was modified since.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterDepartment, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> Update(
        int id, [FromBody] UpdateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await _departmentService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<DepartmentDto>.Ok(department, "Department updated successfully."));
    }

    /// <summary>
    /// Deactivates a department - DELETE means SOFT deactivation (IsActive = false); the row is kept and no
    /// column other than the status and its update audit columns changes. 409 if it is already inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterDepartment, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<DepartmentDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var department = await _departmentService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<DepartmentDto>.Ok(department, "Department deactivated successfully."));
    }
}
