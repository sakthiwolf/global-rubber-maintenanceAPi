namespace GlobalRubber.MMM.Infrastructure.Identity;

/// <summary>
/// Bound from the "Jwt" configuration section. Referenced by both this project (to generate
/// tokens in <see cref="JwtTokenService"/>) and the Api project (to configure JwtBearer token
/// validation) - Api already references Infrastructure, so a single shared class here avoids
/// defining the same settings twice.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;

    /// <summary>Never committed with a real value in appsettings.json - supplied via
    /// appsettings.Development.json for local dev, or environment/user-secrets otherwise.</summary>
    public string Secret { get; init; } = string.Empty;

    public int ExpirationMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime (system analysis 17.5: "rotating refresh token (7 days)").</summary>
    public int RefreshTokenDays { get; init; } = 7;
}
