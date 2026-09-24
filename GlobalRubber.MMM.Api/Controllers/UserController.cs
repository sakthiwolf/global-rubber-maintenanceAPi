using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// User endpoints (security.user_master): list, retrieve, create and update. Every endpoint requires an
/// AdminUsers permission (View for reads, Add, Edit) via [RequirePermission] - never a role-name check.
/// Responses never carry the password or its hash.
/// </summary>
/// <remarks>
/// Overrides the inherited <c>api/v1/[controller]</c> template with an explicit plural route:
/// the class is named <c>UserController</c> (singular, matching the entity it serves) but the
/// REST resource path follows the project's plural convention (<c>/api/v1/users</c>, per
/// CLAUDE.md section 29's examples), which the <c>[controller]</c> token alone cannot produce.
/// </remarks>
[Route("api/v1/users")]
public sealed class UserController : BaseApiController
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>Returns a paged list of users.</summary>
    [HttpGet]
    [RequirePermission(ModuleCodes.AdminUsers, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDto>>>> GetAll(
        [FromQuery] PaginationRequest request, CancellationToken cancellationToken)
    {
        var result = await _userService.GetAllAsync(request, cancellationToken);

        return Ok(ApiResponse<PagedResult<UserDto>>.Ok(result, "Users retrieved successfully."));
    }

    /// <summary>Returns a single user by id, including the rowVersion needed to update it. 404 if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [RequirePermission(ModuleCodes.AdminUsers, PermissionAction.View)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<UserDto>.Ok(user, "User retrieved successfully."));
    }

    /// <summary>
    /// Creates a user. The password is hashed with the existing IPasswordHasher and never stored, logged,
    /// audited or returned; the user code is issued from the USER document sequence. 409 for a duplicate
    /// login ID; 404 if the role does not exist; 400 if it is not active or a field is invalid.
    /// </summary>
    [HttpPost]
    [RequirePermission(ModuleCodes.AdminUsers, PermissionAction.Add)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create(
        [FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _userService.CreateAsync(
            request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = user.UserId }, ApiResponse<UserDto>.Ok(user, "User created successfully."));
    }

    /// <summary>
    /// Updates a user's profile fields, role and status; the password changes only when newPassword is
    /// supplied. Requires the rowVersion from the last read: 409 if the user was modified since.
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(ModuleCodes.AdminUsers, PermissionAction.Edit)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(
        int id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await _userService.UpdateAsync(
            id, request, GetAuthenticatedUserId(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        return Ok(ApiResponse<UserDto>.Ok(user, "User updated successfully."));
    }
}
