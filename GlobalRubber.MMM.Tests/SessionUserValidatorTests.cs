using GlobalRubber.MMM.Application.Services;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// C2: the per-request re-check behind every access token (wired into JWT validation). Users: 1 Admin (ADMIN), 2 Ravi
/// (MAINT_MANAGER); role 4 OLD_ROLE is inactive.
/// </summary>
public class SessionUserValidatorTests
{
    private static (SessionUserValidator Validator, List<GlobalRubber.MMM.Domain.Entities.User> Users) Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = UserTestData.Users();
        return (new SessionUserValidator(new InMemoryUserRepository(users, roles.Find)), users);
    }

    [Fact]
    public async Task AnActiveUser_KeepsTheirRole()
    {
        var (validator, _) = Create();

        Assert.Equal("ADMIN", await validator.GetCurrentRoleCodeAsync(1, "ADMIN", CancellationToken.None));
    }

    [Fact]
    public async Task ADeactivatedUser_IsRefused_EvenWithAValidToken()
    {
        var (validator, users) = Create();
        users.Single(u => u.UserId == 2).IsActive = false;

        Assert.Null(await validator.GetCurrentRoleCodeAsync(2, "MAINT_MANAGER", CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownUser_IsRefused()
    {
        var (validator, _) = Create();

        Assert.Null(await validator.GetCurrentRoleCodeAsync(999, "ADMIN", CancellationToken.None));
    }

    [Fact]
    public async Task AChangedRole_TakesEffectImmediately()
    {
        var (validator, users) = Create();
        users.Single(u => u.UserId == 1).RoleId = 2; // was ADMIN when the token was issued

        Assert.Equal("MAINT_MANAGER", await validator.GetCurrentRoleCodeAsync(1, "ADMIN", CancellationToken.None));
    }

    [Fact]
    public async Task AUserWhoseRoleIsInactive_IsRefused()
    {
        var (validator, users) = Create();
        users.Single(u => u.UserId == 2).RoleId = 4; // OLD_ROLE, inactive

        Assert.Null(await validator.GetCurrentRoleCodeAsync(2, "MAINT_MANAGER", CancellationToken.None));
    }
}
