using System.Net.Http.Headers;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared helpers for tests that call [RequirePermission]-protected endpoints.</summary>
internal static class TestAuth
{
    /// <summary>
    /// A genuinely valid, signed access token from the real IJwtTokenService (no database needed):
    /// "sub" = <paramref name="userId"/>, role claim = <paramref name="roleCode"/>.
    /// </summary>
    public static string MintToken(WebApplicationFactory<Program> factory, string roleCode = "ADMIN", int userId = 1)
    {
        var (token, _) = factory.Services.GetRequiredService<IJwtTokenService>().GenerateAccessToken(new User
        {
            UserId = userId,
            LoginId = "test-user",
            Role = new Role { RoleCode = roleCode, RoleName = roleCode },
        });

        return token;
    }

    public static void Authenticate(
        HttpClient client, WebApplicationFactory<Program> factory, string roleCode = "ADMIN", int userId = 1) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintToken(factory, roleCode, userId));
}

/// <summary>
/// Stand-in for the real permission_master lookup (which needs a live SQL Server): grants either
/// everything, nothing, or exactly the (roleCode, module, action) combinations given - and records
/// every check so tests can assert WHICH permission an endpoint asked for and for WHICH role.
/// </summary>
internal sealed class StubPermissionAuthorization : IPermissionAuthorizationService
{
    private readonly Func<string, string, PermissionAction, bool> _decide;

    public StubPermissionAuthorization(bool grantAll)
        : this((_, _, _) => grantAll)
    {
    }

    public StubPermissionAuthorization(Func<string, string, PermissionAction, bool> decide)
    {
        _decide = decide;
    }

    public List<(string RoleCode, string ModuleCode, PermissionAction Action)> Checks { get; } = new();

    public Task<bool> HasPermissionAsync(
        string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken)
    {
        lock (Checks)
        {
            Checks.Add((roleCode, moduleCode, action));
        }

        return Task.FromResult(_decide(roleCode, moduleCode, action));
    }
}
