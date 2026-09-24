using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Employee endpoints (masters.employee_master): list, retrieve, create, update and deactivate. Every endpoint
/// requires a MasterEmployee permission (View for reads, Add, Edit, Delete for deactivation) via [RequirePermission] -
/// never a role-name check. DELETE is a soft deactivation; nothing is ever physically deleted.
/// </summary>
[Route("api/v1/employees")]
public sealed class EmployeeController : BaseApiController
{
    private readonly IEmployeeService _employeeService;

    public EmployeeController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    /// <summary>Returns a paged list of employees (with department name), optionally filtered by search text (code/name) and active status.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.MasterEmployee, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EmployeeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeDto>>>> GetAll(
        [FromQuery] EmployeeListQuery query, CancellationToken cancellationToken)
    {
        var result = await _employeeService.GetAllAsync(query, cancellationToken);

        return Ok(ApiResponse<PagedResult<EmployeeDto>>.Ok(result, "Employees retrieved successfully."));
    }

    /// <summary>Returns one employee by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.MasterEmployee, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<EmployeeDto>.Ok(employee, "Employee retrieved successfully."));
    }

    /// <summary>
    /// Creates an active employee; its code is issued from the EMPLOYEE document sequence. 400 for an invalid field or
    /// an inactive department; 404 if the department does not exist.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.MasterEmployee, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Create(
        [FromBody] CreateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = employee.EmployeeId }, ApiResponse<EmployeeDto>.Ok(employee, "Employee created successfully."));
    }

    /// <summary>
    /// Updates name, designation, department, mobile and email (the code and status are not editable here). Requires the
    /// rowVersion from the last read: 409 if the employee was modified since.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.MasterEmployee, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Update(
        int id, [FromBody] UpdateEmployeeRequest request, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<EmployeeDto>.Ok(employee, "Employee updated successfully."));
    }

    /// <summary>
    /// Deactivates an employee - DELETE means SOFT deactivation (IsActive = false); the row is kept. 409 if it is
    /// already inactive.
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.MasterEmployee, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var employee = await _employeeService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<EmployeeDto>.Ok(employee, "Employee deactivated successfully."));
    }
}
