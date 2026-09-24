using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Proves an unhandled exception still produces the standard ApiResponse 500 shape after Step
/// 10's change (GlobalExceptionHandler now also attempts to persist an audit.application_log
/// row). The attempted DB write genuinely fails in this environment (no live SQL Server), which
/// is exactly what proves the write is best-effort: the response below must still come back
/// clean rather than a raw 500 from the logging attempt itself.
/// </summary>
public class GlobalExceptionHandlerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public GlobalExceptionHandlerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UnhandledException_StillReturnsStandardApiResponse500_AndApplicationLogWriteFailureDoesNotSurface()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserService>();
                services.AddSingleton<IUserService>(new ThrowingUserService());
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(new AlwaysAllowPermissionAuthorizationService());
            });
        });

        var client = factory.CreateClient();
        var jwtTokenService = factory.Services.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtTokenService.GenerateAccessToken(new User
        {
            UserId = 1,
            LoginId = "test-user",
            Role = new Role { RoleCode = "ADMIN", RoleName = "Administrator" },
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("An unexpected error occurred. Please try again later.", root.GetProperty("message").GetString());
        // No stack trace, exception type name, or internal details leaked to the client.
        Assert.DoesNotContain("ThrowingUserService", body);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingUserService : IUserService
    {
        public Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated unexpected failure from ThrowingUserService.");

        public Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");

        public Task<UserDto> CreateAsync(CreateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");

        public Task<UserDto> UpdateAsync(int userId, UpdateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");
    }

    private sealed class AlwaysAllowPermissionAuthorizationService : IPermissionAuthorizationService
    {
        public Task<bool> HasPermissionAsync(
            string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
