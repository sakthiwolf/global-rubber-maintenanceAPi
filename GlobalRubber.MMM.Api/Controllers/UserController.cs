using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// Read-only user endpoints (security.user_master). Create/update/activate/password/role
/// assignment are separate, later steps - this controller only lists and retrieves users.
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

    /// <summary>
    /// Returns a paged list of users. This is Step 8's one controlled, proven example of
    /// [RequirePermission] - GetById and every other controller remain unsecured until a later
    /// step rolls out authorization everywhere.
    /// </summary>
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

    /// <summary>Returns a single user by id. 404 (via GlobalExceptionHandler) if it does not exist.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserDto>>> GetById(int id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);

        return Ok(ApiResponse<UserDto>.Ok(user, "User retrieved successfully."));
    }
}
