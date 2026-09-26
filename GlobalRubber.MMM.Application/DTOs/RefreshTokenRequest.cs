namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Body of POST /api/v1/auth/refresh and POST /api/v1/auth/logout.</summary>
public sealed class RefreshTokenRequest
{
    public string? RefreshToken { get; init; }
}
