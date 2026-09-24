namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// No [Required] data-annotation validation here deliberately: ASP.NET Core's automatic
/// model-state validation returns its own ValidationProblemDetails shape, bypassing the
/// project's ApiResponse envelope entirely. AuthService validates these fields manually instead
/// so every failure - missing fields or wrong credentials - goes through the same
/// ValidationException -&gt; GlobalExceptionHandler -&gt; ApiResponse path.
/// </summary>
public sealed class LoginRequest
{
    public string LoginId { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
