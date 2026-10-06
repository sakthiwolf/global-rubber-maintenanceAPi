using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Mould F01 fields (migration 024): PartNo (100), PartDescription (250), Ownership (100) - all optional, trimmed, blank
/// stored as null, never part of any uniqueness rule. See <see cref="MoldTestData"/> for the molds.
/// </summary>
public class MoldF01FieldsTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MoldF01FieldsTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Sut(MoldService Service, InMemoryMoldRepository Molds, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var moldList = MoldTestData.Molds();
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var audit = new RecordingAuditLog();
        var evaluator = MoldPmTestFactory.Evaluator(new InMemoryMoldPmRepository(moldList), new InMemoryNotificationRepository(), new FixedClock(), audit);
        return new Sut(new MoldService(molds, evaluator, products, employees, users, new FixedClock(), audit, NullLogger<MoldService>.Instance), molds, audit);
    }

    private static CreateMoldRequest NewMold(string? partNo = null, string? partDescription = null, string? ownership = null, string name = "Bush Mold C") => new()
    {
        MoldName = name, ProductId = 2, MoldType = "Injection", CavityCount = 2, MaximumShots = 500000, WarningShots = 450000,
        ReplacementShots = 500000, Status = "Available", PartNo = partNo, PartDescription = partDescription, Ownership = ownership,
    };

    private static UpdateMoldRequest Edit(InMemoryMoldRepository repo, int id, string? partNo, string? partDescription, string? ownership)
    {
        var s = repo.Stored(id);
        return new UpdateMoldRequest
        {
            MoldName = s.MoldName, ProductId = s.ProductId, MoldType = s.MoldType, CavityCount = s.CavityCount, Manufacturer = s.Manufacturer,
            SerialNumber = s.SerialNumber, Location = s.Location, StorageLocation = s.StorageLocation, CommissionDate = s.CommissionDate,
            MaximumShots = s.MaximumShots, WarningShots = s.WarningShots, ReplacementShots = s.ReplacementShots,
            MaintenanceFrequencyShots = s.MaintenanceFrequencyShots, PmWarningShots = s.PmWarningShots, CurrentUsageShots = s.CurrentUsageShots,
            ResponsibleEmployeeId = s.ResponsibleEmployeeId, Status = s.Status, Remarks = s.Remarks,
            PartNo = partNo, PartDescription = partDescription, Ownership = ownership, RowVersion = Convert.ToBase64String(s.RowVersion),
        };
    }

    private static (string?, string?, string?) F01(MoldDto d) => (d.PartNo, d.PartDescription, d.Ownership);

    [Fact] // 1
    public async Task Create_WithAllThreeFields_StoresAndReturnsThem()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(NewMold("GR-PN-101", "Bush 40mm, NBR", "GRP"), 1, null, CancellationToken.None);

        Assert.Equal(("GR-PN-101", "Bush 40mm, NBR", "GRP"), F01(dto));
        var stored = s.Molds.Stored(dto.MoldId);
        Assert.Equal(("GR-PN-101", "Bush 40mm, NBR", "GRP"), (stored.PartNo, stored.PartDescription, stored.Ownership));
        Assert.Equal(("GR-PN-101", "Bush 40mm, NBR", "GRP"), F01(await s.Service.GetByIdAsync(dto.MoldId, CancellationToken.None)));
    }

    [Fact] // 2
    public async Task Create_WithoutTheFields_StoresNull()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(NewMold(), 1, null, CancellationToken.None);
        Assert.Equal(((string?)null, (string?)null, (string?)null), F01(dto));
    }

    [Fact] // 3 + audit
    public async Task Update_ChangesTheFields_AndAuditsEachOne()
    {
        var s = Create();
        var dto = await s.Service.UpdateAsync(1, Edit(s.Molds, 1, "PN-1", "Seal ring", "DAI"), 1, null, CancellationToken.None);

        Assert.Equal(("PN-1", "Seal ring", "DAI"), F01(dto));
        var details = s.Audit.Entries.Last().Details;
        Assert.Contains(details, d => d is { FieldName: "part_no", OldValue: null, NewValue: "PN-1" });
        Assert.Contains(details, d => d is { FieldName: "part_description", NewValue: "Seal ring" });
        Assert.Contains(details, d => d is { FieldName: "ownership", NewValue: "DAI" });
    }

    [Fact] // 4
    public async Task Update_ClearingTheFields_StoresNull()
    {
        var s = Create();
        await s.Service.UpdateAsync(1, Edit(s.Molds, 1, "PN-1", "Seal ring", "DAI"), 1, null, CancellationToken.None);

        var dto = await s.Service.UpdateAsync(1, Edit(s.Molds, 1, null, "", "   "), 1, null, CancellationToken.None);

        Assert.Equal(((string?)null, (string?)null, (string?)null), F01(dto));
        var stored = s.Molds.Stored(1);
        Assert.Equal(((string?)null, (string?)null, (string?)null), (stored.PartNo, stored.PartDescription, stored.Ownership));
    }

    [Fact] // 5 + 6
    public async Task Values_AreTrimmed_AndBlankBecomesNull()
    {
        var s = Create();
        var trimmed = await s.Service.CreateAsync(NewMold("  PN-7 ", "\tBush \n", " GRP "), 1, null, CancellationToken.None);
        Assert.Equal(("PN-7", "Bush", "GRP"), F01(trimmed));

        var blank = await s.Service.CreateAsync(NewMold("", "  ", "\t", name: "Bush Mold D"), 1, null, CancellationToken.None);
        Assert.Equal(((string?)null, (string?)null, (string?)null), F01(blank));
        var stored = s.Molds.Stored(blank.MoldId);
        Assert.Equal(((string?)null, (string?)null, (string?)null), (stored.PartNo, stored.PartDescription, stored.Ownership));
    }

    [Fact] // 7
    public async Task MaximumLengths_AreEnforced_AndExactLimitsAreAccepted()
    {
        var s = Create();
        var tooLong = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(NewMold(new string('P', 101), new string('D', 251), new string('O', 101)), 1, null, CancellationToken.None));
        Assert.Contains("PartNo must be at most 100 characters.", tooLong.Errors);
        Assert.Contains("PartDescription must be at most 250 characters.", tooLong.Errors);
        Assert.Contains("Ownership must be at most 100 characters.", tooLong.Errors);
        Assert.Empty(s.Audit.Entries); // nothing written

        // Trimming happens before the length check: surrounding spaces do not count.
        var atLimit = await s.Service.CreateAsync(NewMold(" " + new string('P', 100) + " ", new string('D', 250), new string('O', 100), name: "Bush Mold E"), 1, null, CancellationToken.None);
        Assert.Equal((100, 250, 100), (atLimit.PartNo!.Length, atLimit.PartDescription!.Length, atLimit.Ownership!.Length));

        await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Molds, 1, null, new string('D', 251), null), 1, null, CancellationToken.None));
    }

    [Fact] // 8 + 10: list carries them; existing molds untouched
    public async Task List_ReturnsTheFields_AndExistingMoldsHaveNull()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(NewMold("PN-9", "Gasket", "GRP"), 1, null, CancellationToken.None);

        var items = (await s.Service.GetAllAsync(new MoldListQuery { PageNumber = 1, PageSize = 50 }, CancellationToken.None)).Items;

        Assert.Equal(("PN-9", "Gasket", "GRP"), F01(items.Single(i => i.MoldId == created.MoldId)));
        Assert.All(items.Where(i => i.MoldId != created.MoldId), i => Assert.Equal(((string?)null, (string?)null, (string?)null), F01(i)));
    }

    // ================================================================ HTTP: the JSON contract

    [Fact]
    public async Task Http_PostPutGet_CarryTheFields_AndOverlongIs400()
    {
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var moldList = MoldTestData.Molds();
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMoldPmRepository>();
            services.AddSingleton<IMoldPmRepository>(new InMemoryMoldPmRepository(moldList));
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(new InMemoryNotificationRepository());
            services.RemoveAll<IMoldRepository>();
            services.AddSingleton<IMoldRepository>(molds);
            services.RemoveAll<IProductRepository>();
            services.AddSingleton<IProductRepository>(products);
            services.RemoveAll<IEmployeeRepository>();
            services.AddSingleton<IEmployeeRepository>(employees);
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new InMemoryUserRepository(UserTestData.Users(), roles.Find));
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
            services.RemoveAll<IPermissionAuthorizationService>();
            services.AddSingleton<IPermissionAuthorizationService>(new StubPermissionAuthorization(true));
        }));
        var client = factory.CreateClient();
        TestAuth.Authenticate(client, factory, "ADMIN", 1);
        static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
        static async Task<JsonElement> Data(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

        var created = await client.PostAsync("/api/v1/molds", Json(new
        {
            moldName = "Bush Mold C", productId = 2, moldType = "Injection", cavityCount = 2, maximumShots = 500000, warningShots = 450000,
            replacementShots = 500000, status = "Available", partNo = " PN-1 ", partDescription = "Bush", ownership = "GRP",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = await Data(created);
        Assert.Equal(("PN-1", "Bush", "GRP"), (data.GetProperty("partNo").GetString(), data.GetProperty("partDescription").GetString(), data.GetProperty("ownership").GetString()));

        var id = data.GetProperty("moldId").GetInt32();
        var put = await client.PutAsync($"/api/v1/molds/{id}", Json(new
        {
            moldName = "Bush Mold C", productId = 2, moldType = "Injection", cavityCount = 2, maximumShots = 500000, warningShots = 450000,
            replacementShots = 500000, status = "Available", partNo = (string?)null, partDescription = "", ownership = "DAI",
            rowVersion = data.GetProperty("rowVersion").GetString(),
        }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var got = await Data(await client.GetAsync($"/api/v1/molds/{id}"));
        Assert.Equal(JsonValueKind.Null, got.GetProperty("partNo").ValueKind);
        Assert.Equal(JsonValueKind.Null, got.GetProperty("partDescription").ValueKind);
        Assert.Equal("DAI", got.GetProperty("ownership").GetString());

        var overlong = await client.PostAsync("/api/v1/molds", Json(new
        {
            moldName = "Bush Mold D", productId = 2, moldType = "Injection", maximumShots = 500000, warningShots = 450000,
            replacementShots = 500000, status = "Available", partNo = new string('P', 101),
        }));
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.Contains("PartNo must be at most 100 characters.", await overlong.Content.ReadAsStringAsync());
    }
}
