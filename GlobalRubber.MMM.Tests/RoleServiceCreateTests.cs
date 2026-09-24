using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// CreateAsync tests for the real RoleService. Partial-class extension of
/// <see cref="RoleServiceTests"/> so it shares that file's fakes (repositories, audit service,
/// fixed clock) instead of duplicating them.
/// </summary>
public partial class RoleServiceTests
{
    private static CreateRoleRequest ValidRequest() => new()
    {
        RoleCode = "MAINT_SUPERVISOR",
        RoleName = "Maintenance Supervisor",
        Description = "Maintenance supervisor role",
    };

    [Fact]
    public async Task CreateAsync_CreatesNonSystemRole_WithGeneratedId_AndAuditFields()
    {
        var roles = SampleRoles();
        var service = CreateService(roles);

        var dto = await service.CreateAsync(ValidRequest(), actingUserId: 7, ipAddress: null, CancellationToken.None);

        Assert.True(dto.RoleId > 0);
        Assert.Equal("MAINT_SUPERVISOR", dto.RoleCode);
        Assert.Equal("Maintenance Supervisor", dto.RoleName);
        Assert.Equal("Maintenance supervisor role", dto.Description);
        Assert.False(dto.IsSystemRole);
        Assert.True(dto.IsActive);

        var saved = Assert.Single(roles, r => r.RoleCode == "MAINT_SUPERVISOR");
        Assert.Equal(7, saved.CreatedBy);
        Assert.Equal(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc), saved.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_NewRole_IsRetrievableByGetById_AndAppearsInGetAll()
    {
        var service = CreateService(SampleRoles());

        var created = await service.CreateAsync(ValidRequest(), 7, null, CancellationToken.None);

        var fetched = await service.GetByIdAsync(created.RoleId, CancellationToken.None);
        Assert.Equal("MAINT_SUPERVISOR", fetched.RoleCode);

        var all = await service.GetAllAsync(new PaginationRequest { PageNumber = 1, PageSize = 10 }, CancellationToken.None);
        Assert.Equal(3, all.TotalCount);
        Assert.Contains(all.Items, r => r.RoleCode == "MAINT_SUPERVISOR");
    }

    [Fact]
    public async Task CreateAsync_TrimsFields_AndUpperCasesRoleCode()
    {
        var service = CreateService(SampleRoles());

        var dto = await service.CreateAsync(
            new CreateRoleRequest { RoleCode = "  maint_supervisor  ", RoleName = "  Maintenance Supervisor  ", Description = "  desc  " },
            7, null, CancellationToken.None);

        Assert.Equal("MAINT_SUPERVISOR", dto.RoleCode);
        Assert.Equal("Maintenance Supervisor", dto.RoleName);
        Assert.Equal("desc", dto.Description);
    }

    [Fact]
    public async Task CreateAsync_TreatsBlankDescriptionAsNull()
    {
        var service = CreateService(SampleRoles());

        var dto = await service.CreateAsync(
            new CreateRoleRequest { RoleCode = "X_ROLE", RoleName = "X Role", Description = "   " },
            7, null, CancellationToken.None);

        Assert.Null(dto.Description);
    }

    [Fact]
    public async Task CreateAsync_HonoursIsActiveFalse()
    {
        var service = CreateService(SampleRoles());

        var dto = await service.CreateAsync(
            new CreateRoleRequest { RoleCode = "X_ROLE", RoleName = "X Role", IsActive = false },
            7, null, CancellationToken.None);

        Assert.False(dto.IsActive);
    }

    [Theory]
    [InlineData("MAINT_MANAGER")]
    [InlineData("maint_manager")] // normalized to upper case before the uniqueness check
    [InlineData("  admin  ")]
    public async Task CreateAsync_RejectsDuplicateRoleCode_WithConflict(string code)
    {
        var roles = SampleRoles();
        var service = CreateService(roles);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = code, RoleName = "Brand New Name" }, 7, null, CancellationToken.None));

        Assert.Equal(2, roles.Count);
    }

    [Theory]
    [InlineData("Maintenance Manager")]
    [InlineData("maintenance manager")]
    [InlineData("  ADMINISTRATOR ")]
    public async Task CreateAsync_RejectsDuplicateRoleName_CaseInsensitively_WithConflict(string name)
    {
        var roles = SampleRoles();
        var service = CreateService(roles);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "BRAND_NEW", RoleName = name }, 7, null, CancellationToken.None));

        Assert.Equal(2, roles.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsMissingRoleName_WithValidationError(string name)
    {
        var service = CreateService(SampleRoles());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "OK_CODE", RoleName = name }, 7, null, CancellationToken.None));

        Assert.Contains("RoleName is required.", ex.Errors);
    }

    [Fact]
    public async Task CreateAsync_RejectsMissingRoleCode_WithValidationError()
    {
        var service = CreateService(SampleRoles());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "  ", RoleName = "Some Name" }, 7, null, CancellationToken.None));

        Assert.Contains("RoleCode is required.", ex.Errors);
    }

    [Theory]
    [InlineData("HAS SPACE")]
    [InlineData("HAS-DASH")]
    [InlineData("HAS.DOT")]
    [InlineData("CAFÉ")]
    public async Task CreateAsync_RejectsRoleCodeWithInvalidCharacters(string code)
    {
        var service = CreateService(SampleRoles());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = code, RoleName = "Some Name" }, 7, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("RoleCode may contain only"));
    }

    [Fact]
    public async Task CreateAsync_AcceptsRoleCodeOfExactlyMaxLength_AndRejectsOneOver()
    {
        var service = CreateService(SampleRoles());

        var ok = await service.CreateAsync(
            new CreateRoleRequest { RoleCode = new string('A', 30), RoleName = "Thirty" }, 7, null, CancellationToken.None);
        Assert.Equal(30, ok.RoleCode.Length);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = new string('B', 31), RoleName = "Thirty One" }, 7, null, CancellationToken.None));
        Assert.Contains(ex.Errors, e => e.Contains("at most 30"));
    }

    [Fact]
    public async Task CreateAsync_RejectsOverlongRoleNameAndDescription_ReportingAllErrorsTogether()
    {
        var service = CreateService(SampleRoles());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "", RoleName = new string('n', 101), Description = new string('d', 201) },
            7, null, CancellationToken.None));

        Assert.Equal(3, ex.Errors.Count);
    }

    [Fact]
    public async Task CreateAsync_WritesRoleCreatedAuditEntry_WithUserIpAndRoleDetails()
    {
        var audit = new FakeAuditLogService();
        var service = CreateService(SampleRoles(), audit);

        var dto = await service.CreateAsync(ValidRequest(), actingUserId: 7, ipAddress: "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Equal(7, entry.UserId);
        Assert.Equal("Francis Xavier", entry.UserName);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("RoleCreated", entry.Action);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(dto.RoleId, entry.EntityId);
        Assert.Equal("MAINT_SUPERVISOR", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Maintenance Supervisor", entry.Description);
    }

    [Fact]
    public async Task CreateAsync_StillCreatesAndAudits_WhenActingUserCannotBeResolved()
    {
        var audit = new FakeAuditLogService();
        var service = CreateService(SampleRoles(), audit);

        await service.CreateAsync(ValidRequest(), actingUserId: null, ipAddress: null, CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Null(entry.UserId);
        Assert.Equal("Unknown", entry.UserName);
    }

    [Fact]
    public async Task CreateAsync_WritesNoAuditEntry_WhenCreationFails()
    {
        var audit = new FakeAuditLogService();
        var service = CreateService(SampleRoles(), audit);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "ADMIN", RoleName = "Whatever" }, 7, null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateRoleRequest { RoleCode = "", RoleName = "" }, 7, null, CancellationToken.None));

        Assert.Empty(audit.LoggedEntries);
    }
}
