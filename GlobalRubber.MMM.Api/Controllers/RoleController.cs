using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Read-only role endpoints (security.role_master). Create/update/permission-matrix are
/// separate, later steps - this controller only lists and retrieves roles. Same pattern as
/// <see cref="UserController"/>: explicit plural route overriding the inherited singular
/// <c>api/v1/[controller]</c> template.
/// </summary>
[Route("api/v1/roles")]
public sealed class RoleController : BaseApiController
{
    private readonly IRoleService _roleService;
    private readonly IPermissionService _permissionService;

    public RoleController(IRoleService roleService, IPermissionService permissionService)
    {
        _roleService = roleService;
        _permissionService = permissionService;
    }

    /// <summary>Returns a paged list of roles.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RoleDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<RoleDto>>>> GetAll(
        [FromQuery] PaginationRequest request, CancellationToken cancellationToken)
    {
        var result = await _roleService.GetAllAsync(request, cancellationToken);

        return Ok(ApiResponse<PagedResult<RoleDto>>.Ok(result, "Roles retrieved successfully."));
    }

    /// <summary>Returns a single role by id. 404 (via GlobalExceptionHandler) if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var role = await _roleService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<RoleDto>.Ok(role, "Role retrieved successfully."));
    }

    /// <summary>Returns the complete permission matrix (all active modules) for one role.</summary>
    [HttpGet("{roleId:int}/permissions")]
    [ProducesResponseType(typeof(ApiResponse<RolePermissionMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RolePermissionMatrixDto>>> GetPermissions(
        int roleId, CancellationToken cancellationToken)
    {
        var matrix = await _permissionService.GetRolePermissionsAsync(roleId, cancellationToken);

        return Ok(ApiResponse<RolePermissionMatrixDto>.Ok(matrix, "Role permissions retrieved successfully."));
    }

    /// <summary>
    /// Saves the permission matrix for one role. Only the modules included in the request are
    /// changed - "Select All"/"Clear All" are frontend-only conveniences that simply mean every
    /// module is included with the same value.
    /// </summary>
    [HttpPut("{roleId:int}/permissions")]
    [ProducesResponseType(typeof(ApiResponse<RolePermissionMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RolePermissionMatrixDto>>> UpdatePermissions(
        int roleId, [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var matrix = await _permissionService.UpdateRolePermissionsAsync(roleId, request, cancellationToken);

        return Ok(ApiResponse<RolePermissionMatrixDto>.Ok(matrix, "Role permissions updated successfully."));
    }
}
