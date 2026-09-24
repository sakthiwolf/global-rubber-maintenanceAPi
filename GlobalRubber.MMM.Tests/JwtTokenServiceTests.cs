using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GlobalRubber.MMM.Tests;

/// <summary>Tests the real JwtTokenService - it has no DB dependency, so this exercises actual code.</summary>
public class JwtTokenServiceTests
{
    private const string Secret = "unit-test-signing-key-at-least-32-characters-long";
    private const string Issuer = "GlobalRubberMMM.Tests";
    private const string Audience = "GlobalRubberMMM.Tests.Client";

    private static User SampleUser() => new()
    {
        UserId = 7,
        UserCode = "USR-0007",
        LoginId = "engineer",
        UserName = "Ravi Kumar",
        RoleId = 3,
        Role = new Role { RoleId = 3, RoleCode = "MAINT_ENGINEER", RoleName = "Maintenance Engineer" },
    };

    private static JwtTokenService CreateService(int expirationMinutes = 15) =>
        new(
            Options.Create(new JwtOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                Secret = Secret,
                ExpirationMinutes = expirationMinutes,
            }),
            new FixedDateTimeProvider(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void GenerateAccessToken_ReturnsTokenWithOnlyIntendedClaims()
    {
        var service = CreateService();

        var (token, _) = service.GenerateAccessToken(SampleUser());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("7", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal("engineer", jwt.Claims.Single(c => c.Type == "login_id").Value);
        Assert.Equal("MAINT_ENGINEER", jwt.Claims.Single(c => c.Type == ClaimTypes.Role).Value);
        Assert.Single(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);

        // Nothing permission-related - permissions stay application data, not token data.
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("permission", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("module", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateAccessToken_ExpiresAtUtc_MatchesConfiguredExpirationMinutes()
    {
        var service = CreateService(expirationMinutes: 15);

        var (_, expiresAtUtc) = service.GenerateAccessToken(SampleUser());

        Assert.Equal(new DateTime(2026, 1, 1, 12, 15, 0, DateTimeKind.Utc), expiresAtUtc);
    }

    [Fact]
    public void GenerateAccessToken_ProducesToken_ThatValidatesSuccessfully_WithMatchingParameters()
    {
        // Uses the real current time as the token's issue time - JwtSecurityTokenHandler
        // validates lifetime against the actual system clock, not the fake IDateTimeProvider,
        // so a token dated in the past (as the other tests' fixed 2026-01-01 clock would
        // produce) would be rejected as expired regardless of ExpirationMinutes.
        var service = new JwtTokenService(
            Options.Create(new JwtOptions { Issuer = Issuer, Audience = Audience, Secret = Secret, ExpirationMinutes = 15 }),
            new FixedDateTimeProvider(DateTime.UtcNow));

        var (token, _) = service.GenerateAccessToken(SampleUser());

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ValidateLifetime = true,
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out var validatedToken);

        Assert.NotNull(validatedToken);
        Assert.Equal("MAINT_ENGINEER", principal.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public void GenerateAccessToken_TokenFailsValidation_WithWrongSigningKey()
    {
        var service = CreateService();

        var (token, _) = service.GenerateAccessToken(SampleUser());

        var wrongKeyParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-signing-key-32chars+")),
            ValidateLifetime = true,
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, wrongKeyParameters, out _));
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public FixedDateTimeProvider(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
