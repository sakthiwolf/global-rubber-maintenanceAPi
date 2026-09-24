using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
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
}
