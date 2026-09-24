using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Unit tests against the real PermissionAuthorizationService with a fake IPermissionRepository
/// - covers all six actions, which no single HTTP endpoint could exercise yet (only GetAll/View
/// is wired to [RequirePermission] so far). See AuthorizationTests.cs for the HTTP-level
/// 401/403/"real endpoint" coverage.
/// </summary>
public class PermissionAuthorizationServiceTests
{
    [Theory]
    [InlineData(PermissionAction.View, true)]
    [InlineData(PermissionAction.Add, true)]
    [InlineData(PermissionAction.Edit, false)]
    [InlineData(PermissionAction.Delete, false)]
    [InlineData(PermissionAction.Approve, false)]
    [InlineData(PermissionAction.Export, true)]
    public async Task HasPermissionAsync_ReturnsTheStoredFlagForEachAction(PermissionAction action, bool expected)
    {
        var repository = new FakePermissionRepository(new PermissionActionFlags(
            CanView: true, CanAdd: true, CanEdit: false, CanDelete: false, CanApprove: false, CanExport: true));
        var service = new PermissionAuthorizationService(repository);

        var allowed = await service.HasPermissionAsync("MAINT_MANAGER", "MASTER_MACHINE", action, CancellationToken.None);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_WhenNoPermissionRowExists()
    {
        // "Missing row means no access" - same rule as Step 6's matrix, not an error.
        var repository = new FakePermissionRepository(flags: null);
        var service = new PermissionAuthorizationService(repository);

        var allowed = await service.HasPermissionAsync("PRODUCTION_USER", "ADMIN_ROLES", PermissionAction.View, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsFalse_ForEmptyRoleCode()
    {
        var repository = new FakePermissionRepository(new PermissionActionFlags(true, true, true, true, true, true));
        var service = new PermissionAuthorizationService(repository);

        var allowed = await service.HasPermissionAsync(string.Empty, "MASTER_MACHINE", PermissionAction.View, CancellationToken.None);

        Assert.False(allowed);
    }

    [Fact]
    public async Task HasPermissionAsync_LooksUpByBothRoleCodeAndModuleCode_NotJustRole()
    {
        // A role granted View on one module must not be treated as granted on a different one.
        var repository = new FakePermissionRepository(new PermissionActionFlags(true, false, false, false, false, false))
        {
            ExpectedModuleCode = "MASTER_MACHINE",
        };
        var service = new PermissionAuthorizationService(repository);

        var allowedForExpectedModule = await service.HasPermissionAsync("MAINT_MANAGER", "MASTER_MACHINE", PermissionAction.View, CancellationToken.None);
        var allowedForDifferentModule = await service.HasPermissionAsync("MAINT_MANAGER", "MASTER_MOLD", PermissionAction.View, CancellationToken.None);

        Assert.True(allowedForExpectedModule);
        Assert.False(allowedForDifferentModule); // repository returns null for any other module code
    }

    private sealed class FakePermissionRepository : IPermissionRepository
    {
        private readonly PermissionActionFlags? _flags;

        public FakePermissionRepository(PermissionActionFlags? flags)
        {
            _flags = flags;
        }

        public string? ExpectedModuleCode { get; init; }

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(string roleCode, string moduleCode, CancellationToken cancellationToken) =>
            Task.FromResult(ExpectedModuleCode is null || ExpectedModuleCode == moduleCode ? _flags : null);

        public Task<IReadOnlyList<Domain.Entities.Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by PermissionAuthorizationServiceTests.");

        public Task<IReadOnlyList<Domain.Entities.Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by PermissionAuthorizationServiceTests.");

        public Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Domain.Entities.Permission> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by PermissionAuthorizationServiceTests.");
    }
}
