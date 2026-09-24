using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GlobalRubber.MMM.Infrastructure.Identity;

/// <summary>
/// Generates JWT access tokens carrying only what authentication/authorization needs: user id,
/// login id and role code. Deliberately does not embed the permission matrix - permissions stay
/// database/application data, evaluated by the authorization layer built in the next step.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly IDateTimeProvider _dateTimeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, IDateTimeProvider dateTimeProvider)
    {
        _options = options.Value;
        _dateTimeProvider = dateTimeProvider;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user)
    {
        var expiresAtUtc = _dateTimeProvider.UtcNow.AddMinutes(_options.ExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new Claim("login_id", user.LoginId),
            new Claim(ClaimTypes.Role, user.Role.RoleCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
