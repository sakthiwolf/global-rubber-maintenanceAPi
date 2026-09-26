using System.Security.Cryptography;
using System.Text;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace GlobalRubber.MMM.Infrastructure.Identity;

/// <summary>
/// 64 random bytes (base64url) per token; stored as the upper-case hex SHA-256 of the token (64 chars, fits
/// token_hash VARCHAR(128)). The lifetime is Jwt:RefreshTokenDays (system analysis 17.5: 7 days).
/// </summary>
public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private readonly JwtOptions _options;

    public RefreshTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, string TokenHash, DateTime ExpiresAtUtc) Create(DateTime nowUtc)
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        return (token, Hash(token), nowUtc.AddDays(_options.RefreshTokenDays));
    }

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
