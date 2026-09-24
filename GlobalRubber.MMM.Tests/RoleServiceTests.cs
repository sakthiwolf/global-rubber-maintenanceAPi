using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Unit tests against a fake <see cref="IRoleRepository"/> - there is no RoleController yet
/// (that's Step 5), so this is the only layer available to exercise right now.
/// </summary>
public class RoleServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsPagedResult_MappedFromRepository()
    {
        var service = new RoleService(new FakeRoleRepository(SampleRoles()));

        var result = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, r => r.RoleCode == "ADMIN" && r.RoleName == "Administrator");
    }

    [Fact]
    public async Task GetAllAsync_RespectsPageSize()
    {
        var service = new RoleService(new FakeRoleRepository(SampleRoles()));

        var result = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 1 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMappedRole_WhenFound()
    {
        var service = new RoleService(new FakeRoleRepository(SampleRoles()));

        var dto = await service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("ADMIN", dto.RoleCode);
        Assert.Equal("Administrator", dto.RoleName);
        Assert.True(dto.IsSystemRole);
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsNotFoundException_WhenRoleDoesNotExist()
    {
        var service = new RoleService(new FakeRoleRepository(SampleRoles()));

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(999, CancellationToken.None));
    }

    private static List<Role> SampleRoles() => new()
    {
        new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator", IsSystemRole = true, IsActive = true },
        new Role { RoleId = 2, RoleCode = "MAINT_MANAGER", RoleName = "Maintenance Manager", IsSystemRole = false, IsActive = true },
    };

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles;

        public FakeRoleRepository(List<Role> roles)
        {
            _roles = roles;
        }

        public Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
            PaginationRequest request, CancellationToken cancellationToken)
        {
            var items = _roles
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            return Task.FromResult<(IReadOnlyList<Role>, int)>((items, _roles.Count));
        }

        public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.FirstOrDefault(r => r.RoleId == roleId));
    }
}
