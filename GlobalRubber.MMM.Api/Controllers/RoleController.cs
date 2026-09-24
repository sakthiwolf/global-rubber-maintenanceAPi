using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Role endpoints (security.role_master): list, retrieve, create, update, deactivate, and the
/// permission matrix. Every endpoint requires an AdminRoles permission (View for reads, Add, Edit,
/// Delete for the matching write) via [RequirePermission] - never a role-name check. Same pattern as
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
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RoleDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<RoleDto>>>> GetAll(
        [FromQuery] PaginationRequest request, CancellationToken cancellationToken)
    {
        var result = await _roleService.GetAllAsync(request, cancellationToken);

        return Ok(ApiResponse<PagedResult<RoleDto>>.Ok(result, "Roles retrieved successfully."));
    }

    /// <summary>Returns a single role by id. 404 (via GlobalExceptionHandler) if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var role = await _roleService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<RoleDto>.Ok(role, "Role retrieved successfully."));
    }

    /// <summary>
    /// Creates a non-system role. The role starts with no permission rows - "missing row means
    /// no access" - so it can do nothing until an administrator configures its permission
    /// matrix through PUT /roles/{roleId}/permissions.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Create(
        [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _roleService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(
            nameof(GetById), new { id = role.RoleId }, ApiResponse<RoleDto>.Ok(role, "Role created successfully."));
    }

    /// <summary>
    /// Updates a role's code, name and description. A system role's code cannot be changed (409);
    /// IsSystemRole/IsActive are never touched here. 404 if the role does not exist, 409 for a
    /// duplicate code/name or if the role was modified by someone else after it was loaded.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Update(
        int id, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await _roleService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<RoleDto>.Ok(role, "Role updated successfully."));
    }

    /// <summary>
    /// Deactivates a role - DELETE means SOFT deactivation (IsActive = false); the row and its
    /// permission rows are never deleted. 409 if the role is a system role, is already inactive,
    /// still has active users assigned, or was modified by someone else after it was loaded.
    /// Returns the standard ApiResponse envelope with the deactivated role (not 204, so the
    /// frontend gets the same envelope and can refresh the role without another call).
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.Delete)]
    [ProducesResponseType(typeof(ApiResponse<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RoleDto>>> Deactivate(int id, CancellationToken cancellationToken)
    {
        var role = await _roleService.DeactivateAsync(
            id, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<RoleDto>.Ok(role, "Role deactivated successfully."));
    }

    /// <summary>Returns the complete permission matrix (all active modules) for one role.</summary>
    [HttpGet("{roleId:int}/permissions")]
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<RolePermissionMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
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
    [RequirePermission(ModuleCodes.AdminRoles, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<RolePermissionMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<RolePermissionMatrixDto>>> UpdatePermissions(
        int roleId, [FromBody] UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        var matrix = await _permissionService.UpdateRolePermissionsAsync(
            roleId, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<RolePermissionMatrixDto>.Ok(matrix, "Role permissions updated successfully."));
    }
}
