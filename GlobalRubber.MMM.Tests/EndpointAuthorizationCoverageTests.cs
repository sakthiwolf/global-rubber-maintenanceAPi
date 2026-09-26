using System.Reflection;
using GlobalRubber.MMM.Api.Authorization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Structural guard for "frontend hiding is NOT security": every endpoint of every controller must either
/// carry [RequirePermission(module, action)] with the action that matches its HTTP verb, or be on the
/// explicit, reviewed list of endpoints that are deliberately open. A new controller (e.g. Machines) that
/// forgets its attribute, or protects a POST with View, fails here.
/// </summary>
public class EndpointAuthorizationCoverageTests
{
    // Deliberately not permission-gated. Anything added here needs a reason:
    //   Auth.Login .............. anonymous by definition (it issues the token)
    //   Auth.GetMyPermissions ... any authenticated user reads their OWN matrix (401 without a token)
    //   Auth.Refresh ............ anonymous by necessity: called when the access token has expired (the refresh token is the credential)
    //   Auth.Logout ............. any authenticated user revokes their OWN refresh token (401 without a token)
    //   System.Ping ............. anonymous liveness probe
    //   Notification.GetMine .... any authenticated user reads THEIR notifications; the service filters them to the
    //                             modules the user's role can View (401 without a token)
    private static readonly HashSet<string> OpenEndpoints = new()
    {
        "AuthController.Login",
        "AuthController.GetMyPermissions",
        "AuthController.Refresh",
        "AuthController.Logout",
        "SystemController.Ping",
        "NotificationController.GetMine",
    };

    private static IEnumerable<(Type Controller, MethodInfo Method, HttpMethodAttribute Http)> Endpoints() =>
        typeof(RequirePermissionAttribute).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(m => (Controller: t, Method: m, Http: m.GetCustomAttributes<HttpMethodAttribute>().FirstOrDefault()))
                .Where(x => x.Http is not null)
                .Select(x => (x.Controller, x.Method, x.Http!)));

    private static string Key((Type Controller, MethodInfo Method, HttpMethodAttribute Http) e) =>
        $"{e.Controller.Name}.{e.Method.Name}";

    private static RequirePermissionAttribute? PermissionOf((Type Controller, MethodInfo Method, HttpMethodAttribute Http) e) =>
        e.Method.GetCustomAttribute<RequirePermissionAttribute>() ?? e.Controller.GetCustomAttribute<RequirePermissionAttribute>();

    [Fact]
    public void ThereAreEndpointsToCheck()
    {
        Assert.NotEmpty(Endpoints());
    }

    [Fact]
    public void EveryEndpoint_HasARequirePermission_OrIsOnTheReviewedOpenList()
    {
        var unprotected = Endpoints()
            .Where(e => PermissionOf(e) is null && !OpenEndpoints.Contains(Key(e)))
            .Select(Key)
            .ToList();

        Assert.True(unprotected.Count == 0, "Endpoints without [RequirePermission]: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void OpenList_OnlyNamesEndpointsThatStillExist()
    {
        var existing = Endpoints().Select(Key).ToHashSet();

        Assert.All(OpenEndpoints, key => Assert.Contains(key, existing));
    }

    [Fact]
    public void NoEndpoint_IsAnonymousExceptTheReviewedOnes()
    {
        var anonymous = Endpoints()
            .Where(e => e.Method.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                        || e.Controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Where(e => PermissionOf(e) is not null)
            .Select(Key)
            .ToList();

        Assert.True(anonymous.Count == 0, "[AllowAnonymous] would bypass [RequirePermission]: " + string.Join(", ", anonymous));
    }

    [Fact]
    public void EveryPermissionGatedEndpoint_UsesTheActionThatMatchesItsHttpVerb()
    {
        // GET -> View, POST -> Add, PUT/PATCH -> Edit, DELETE -> Delete (DELETE here means Deactivate).
        var mismatches = new List<string>();
        foreach (var e in Endpoints())
        {
            var permission = PermissionOf(e);
            if (permission is null)
            {
                continue;
            }

            var verb = e.Http.HttpMethods.Single();
            var expected = verb switch
            {
                "GET" => PermissionAction.View,
                "POST" => PermissionAction.Add,
                "PUT" or "PATCH" => PermissionAction.Edit,
                "DELETE" => PermissionAction.Delete,
                _ => throw new InvalidOperationException($"Unmapped verb {verb} on {Key(e)}"),
            };

            if (permission.Action != expected)
            {
                mismatches.Add($"{Key(e)}: {verb} requires {permission.Action}, expected {expected}");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void EveryPermissionGatedEndpoint_UsesACentralModuleCodeConstant()
    {
        var known = typeof(ModuleCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var unknown = Endpoints()
            .Select(e => (Key: Key(e), Permission: PermissionOf(e)))
            .Where(x => x.Permission is not null && !known.Contains(x.Permission.ModuleCode))
            .Select(x => $"{x.Key} -> '{x.Permission!.ModuleCode}'")
            .ToList();

        Assert.True(unknown.Count == 0, "Module codes not defined in ModuleCodes: " + string.Join(", ", unknown));
    }

    [Fact]
    public void ModuleCodes_HaveNoDuplicateValues()
    {
        var values = typeof(ModuleCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.Equal(values.Count, values.Distinct().Count());
    }
}
