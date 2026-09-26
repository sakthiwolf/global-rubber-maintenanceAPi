using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The REAL production settings file (GlobalRubber.MMM.Api/appsettings.json, loaded exactly as a Production server loads
/// it - no Development file) must let the API start and log users in. Every assertion reports only WHAT is wrong, never
/// a value: this file contains the production connection string and JWT secret, and test output must not leak them.
/// </summary>
public class ProductionConfigurationTests
{
    private const string ProductionFrontendOrigin = "https://globalmaintenance.genuineitsolution.com";

    private static IConfiguration LoadProductionConfiguration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GlobalRubber.MMM.Api", "appsettings.json")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "GlobalRubber.MMM.Api/appsettings.json was not found above the test output folder.");
        var apiDir = Path.Combine(dir!.FullName, "GlobalRubber.MMM.Api");

        // Production = appsettings.json (+ appsettings.Production.json if it exists). NOT the Development file.
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(apiDir, "appsettings.Production.json"), optional: true)
            .Build();
    }

    private static JwtOptions ProductionJwt() =>
        LoadProductionConfiguration().GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

    private sealed class Clock : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = DateTime.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    [Fact]
    public void Jwt_AllRequiredValuesArePresent_AndTheSecretPassesTheStartupValidation()
    {
        var jwt = ProductionJwt();

        Assert.False(string.IsNullOrWhiteSpace(jwt.Issuer), "Jwt:Issuer is missing.");
        Assert.False(string.IsNullOrWhiteSpace(jwt.Audience), "Jwt:Audience is missing.");
        Assert.True(jwt.ExpirationMinutes > 0, "Jwt:ExpirationMinutes must be greater than 0.");
        Assert.True(jwt.RefreshTokenDays > 0, "Jwt:RefreshTokenDays must be greater than 0.");
        // The same rule AddInfrastructure validates on start (a failing rule = HTTP 500.30 "app failed to start").
        Assert.True(!string.IsNullOrWhiteSpace(jwt.Secret) && jwt.Secret.Length >= 32,
            "Jwt:Secret must be configured and at least 32 characters long (value not shown).");
    }

    [Fact]
    public void Jwt_ProductionSettings_IssueATokenThatValidates_WithTheExistingClaims()
    {
        var jwt = ProductionJwt();
        var service = new JwtTokenService(Options.Create(jwt), new Clock());
        var user = new User { UserId = 1, LoginId = "admin", UserName = "Administrator", RoleId = 1, Role = new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator" } };

        var (token, expiresAtUtc) = service.GenerateAccessToken(user);

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
        }, out _);

        Assert.Equal("ADMIN", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal("admin", principal.FindFirst("login_id")?.Value);
        Assert.True(expiresAtUtc > DateTime.UtcNow, "The token must expire in the future.");
        Assert.DoesNotContain(jwt.Secret, token); // the token never carries the secret
    }

    [Fact]
    public void ConnectionString_IsAWellFormedSqlServerConnectionString()
    {
        var cs = LoadProductionConfiguration().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(cs), "ConnectionStrings:DefaultConnection is missing.");

        SqlConnectionStringBuilder? builder = null;
        var parsed = true;
        try
        {
            builder = new SqlConnectionStringBuilder(cs);
        }
        catch (ArgumentException)
        {
            parsed = false; // the message would echo the string - report only that it is malformed
        }

        Assert.True(parsed, "ConnectionStrings:DefaultConnection is not a valid SQL Server connection string (value not shown).");
        Assert.False(string.IsNullOrWhiteSpace(builder!.DataSource), "ConnectionStrings:DefaultConnection has no Server= (Data Source).");
        Assert.False(string.IsNullOrWhiteSpace(builder.InitialCatalog), "ConnectionStrings:DefaultConnection has no Database=.");
    }

    [Fact]
    public void Cors_AllowsTheProductionFrontend_AsPlainAbsoluteUrls()
    {
        var origins = LoadProductionConfiguration().GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        Assert.Contains(ProductionFrontendOrigin, origins, StringComparer.OrdinalIgnoreCase);
        foreach (var origin in origins)
        {
            // No Markdown such as [https://x](https://x), no path, no trailing slash - a CORS origin is scheme://host[:port].
            Assert.True(Uri.TryCreate(origin, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"),
                $"Cors:AllowedOrigins entry '{origin}' is not a plain http(s) URL.");
            Assert.True(uri!.AbsolutePath == "/" && !origin.EndsWith('/') && !origin.Contains('[') && !origin.Contains('('),
                $"Cors:AllowedOrigins entry '{origin}' must be scheme://host[:port] only.");
        }
    }
}
