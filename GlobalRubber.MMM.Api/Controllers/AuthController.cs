using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using System.Security.Claims;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController : BaseApiController
{
    private readonly IAuthService _authService;
    private readonly IPermissionService _permissionService;

    public AuthController(IAuthService authService, IPermissionService permissionService)
    {
        _authService = authService;
        _permissionService = permissionService;
    }

    /// <summary>Authenticates a user by login id/password and returns a JWT access token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login(
        [FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

        var result = await _authService.LoginAsync(request, ipAddress, cancellationToken);

        return Ok(ApiResponse<AuthResponseDto>.Ok(result, "Login successful."));
    }

    /// <summary>
    /// The signed-in user's OWN permission matrix (any authenticated user - "GET /me" in the system
    /// analysis, section 18.1). The frontend builds its menu and gates its actions from this. It is
    /// separate from GET /roles/{roleId}/permissions, which is an administrative read of any role and
    /// needs AdminRoles.View: most roles do not have that, yet every user must be able to load their
    /// own permissions. The role is taken from the token's user, never from the request.
    /// </summary>
    [HttpGet("me/permissions")]
    [ProducesResponseType(typeof(ApiResponse<RolePermissionMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<RolePermissionMatrixDto>>> GetMyPermissions(
        CancellationToken cancellationToken)
    {
        // Authenticated-only: no token / invalid token leaves the JWT "sub" claim absent -> 401 in the
        // standard ApiResponse shape (same exception RequirePermission raises).
        var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : throw new UnauthorizedAccessException();

        var matrix = await _permissionService.GetMyPermissionsAsync(userId, cancellationToken);

        return Ok(ApiResponse<RolePermissionMatrixDto>.Ok(matrix, "Your permissions retrieved successfully."));
    }
}
