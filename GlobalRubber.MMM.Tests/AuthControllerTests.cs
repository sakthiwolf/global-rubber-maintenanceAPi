using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Exercises POST /api/v1/auth/login through the real HTTP pipeline with IAuthService swapped
/// for an in-memory fake, same approach as the other controller test files.
/// </summary>
public class AuthControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithFakeAuthService(FakeAuthService fake) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddSingleton<IAuthService>(fake);
            });
        }).CreateClient();

    [Fact]
    public async Task Login_Returns200_WithTokenAndUser_ForValidCredentials()
    {
        var client = CreateClientWithFakeAuthService(FakeAuthService.AcceptsAdminLogin());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { LoginId = "admin", Password = "correct-password" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        Assert.False(string.IsNullOrEmpty(data.GetProperty("token").GetString()));
        Assert.Equal("admin", data.GetProperty("user").GetProperty("loginId").GetString());

        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_Returns400_ThroughGlobalExceptionHandler_ForInvalidCredentials()
    {
        var client = CreateClientWithFakeAuthService(FakeAuthService.AcceptsAdminLogin());

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { LoginId = "admin", Password = "wrong-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Refresh_IsAnonymous_Returns200_WithANewTokenPair_And401_ForABadToken()
    {
        var client = CreateClientWithFakeAuthService(FakeAuthService.AcceptsAdminLogin()); // no bearer token at all

        var ok = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest { RefreshToken = "good-refresh" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using (var document = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(("new-jwt-token", "next-refresh"), (data.GetProperty("token").GetString(), data.GetProperty("refreshToken").GetString()));
            Assert.True(data.TryGetProperty("refreshTokenExpiresAtUtc", out _));
        }

        var bad = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest { RefreshToken = "used-or-unknown" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.DoesNotContain("Exception", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_RequiresAToken_ThenRevokesTheCallersOwnRefreshToken_204()
    {
        var fake = FakeAuthService.AcceptsAdminLogin();
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthService>();
            services.AddSingleton<IAuthService>(fake);
        }));
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequest { RefreshToken = "rt" })).StatusCode);
        Assert.Empty(fake.Logouts);

        var client = factory.CreateClient();
        TestAuth.Authenticate(client, factory, "ADMIN", 7);
        var response = await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshTokenRequest { RefreshToken = "rt" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(("rt", 7), Assert.Single(fake.Logouts)); // the user id comes from the token, never the body
    }

    private sealed class FakeAuthService : IAuthService
    {
        public static FakeAuthService AcceptsAdminLogin() => new();

        public Task<AuthResponseDto> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
        {
            if (request.LoginId != "admin" || request.Password != "correct-password")
            {
                throw new ValidationException("Invalid login ID or password.");
            }

            return Task.FromResult(new AuthResponseDto
            {
                Token = "fake-jwt-token",
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                User = new UserDto
                {
                    UserId = 1,
                    UserCode = "USR-0001",
                    LoginId = "admin",
                    UserName = "Administrator",
                    RoleId = 1,
                    RoleName = "Administrator",
                    IsActive = true,
                },
            });
        }

        public List<(string? Token, int UserId)> Logouts { get; } = new();

        public Task<AuthResponseDto> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken)
        {
            if (request.RefreshToken != "good-refresh")
            {
                throw new UnauthorizedAccessException("Your session has expired. Please sign in again.");
            }

            return Task.FromResult(new AuthResponseDto
            {
                Token = "new-jwt-token", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15), RefreshToken = "next-refresh", RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                User = new UserDto { UserId = 1, UserCode = "USR-0001", LoginId = "admin", UserName = "Administrator", RoleId = 1, RoleName = "Administrator", IsActive = true },
            });
        }

        public Task LogoutAsync(RefreshTokenRequest request, int userId, string? ipAddress, CancellationToken cancellationToken)
        {
            Logouts.Add((request.RefreshToken, userId));
            return Task.CompletedTask;
        }
    }
}
