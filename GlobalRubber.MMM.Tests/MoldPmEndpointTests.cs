using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// HTTP contract of /api/v1/mold-maintenance and /api/v1/notifications through the real pipeline (JWT, RequirePermission,
/// ApiResponse), with in-memory repositories. No database is touched.
/// </summary>
public class MoldPmEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MoldPmEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMoldPmRepository Pms, InMemoryNotificationRepository Notifications, StubPermissionAuthorization Authorization);

    private sealed class StubPermissionService : IPermissionService
    {
        private readonly IReadOnlyList<ModulePermissionDto> _mine;
        public StubPermissionService(params (string Module, bool View)[] modules) =>
            _mine = modules.Select(m => new ModulePermissionDto { ModuleCode = m.Module, CanView = m.View }).ToList();
        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest r, int? u, string? ip, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken c) => Task.FromResult(new RolePermissionMatrixDto { Permissions = _mine });
    }

    private H CreateHarness(Func<string, string, PermissionAction, bool>? decide = null, bool authenticate = true, StubPermissionService? permissions = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var moldList = new List<Mold>
        {
            new()
            {
                MoldId = 10, MoldCode = "MLD-0010", MoldName = "Seal Mold 10", ProductId = 1, MoldType = "Compression", MaximumShots = 5_000_000,
                WarningShots = 4_000_000, ReplacementShots = 4_500_000, MaintenanceFrequencyShots = 100_000, PmWarningShots = 10_000,
                PmCycleStartShots = 500_000, CurrentUsageShots = 605_000, Status = MoldStatus.InProduction, RowVersion = new byte[] { 1 },
            },
        };
        var pms = new InMemoryMoldPmRepository(moldList);
        pms.AddAutomaticPmAsync(new MoldPm
        {
            MoldId = 10, Category = MoldPmCategory.ShotBased, ScheduledDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330)), MoldUsageAtService = 600_000, // due TODAY (IST): the real clock is used here
            ThresholdShots = 600_000, IntervalShots = 100_000, Status = MoldPmStatus.Scheduled,
        }, CancellationToken.None).GetAwaiter().GetResult();
        var notifications = new InMemoryNotificationRepository();
        notifications.Items.Add(new Notification { NotificationId = 1, NotificationType = NotificationTypes.MoldPmDue, ModuleCode = ModuleCodes.TrnMoldPm, Severity = "Critical", Title = "Mold PM due: MLD-0010", Message = "m", EventKey = "k1", CreatedAt = DateTime.UtcNow });
        notifications.Items.Add(new Notification { NotificationId = 2, NotificationType = NotificationTypes.MoldPmWarning, ModuleCode = ModuleCodes.TrnMachinePm, Severity = "Warning", Title = "Other module", Message = "m", EventKey = "k2", CreatedAt = DateTime.UtcNow });
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMoldPmRepository>();
            services.AddSingleton<IMoldPmRepository>(pms);
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(notifications);
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(users);
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
            services.RemoveAll<IPermissionAuthorizationService>();
            services.AddSingleton<IPermissionAuthorizationService>(authorization);
            if (permissions is not null)
            {
                services.RemoveAll<IPermissionService>();
                services.AddSingleton<IPermissionService>(permissions);
            }
        }));

        var client = factory.CreateClient();
        if (authenticate) TestAuth.Authenticate(client, factory, "ADMIN", 1);
        return new H(client, pms, notifications, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task List_Counts_Usage_ById_WithView()
    {
        var h = CreateHarness();

        var list = await Root(await h.Client.GetAsync("/api/v1/mold-maintenance?bucket=due"));
        var item = list.GetProperty("data").GetProperty("items")[0];
        Assert.Equal("MPMD-0001", item.GetProperty("pmNo").GetString());
        Assert.Equal(600_000, item.GetProperty("thresholdShots").GetInt32());
        Assert.Equal(605_000, item.GetProperty("currentShots").GetInt32());
        Assert.Equal(-5_000, item.GetProperty("remainingShots").GetInt64());
        Assert.True(item.GetProperty("createdBySystem").GetBoolean());

        var counts = (await Root(await h.Client.GetAsync("/api/v1/mold-maintenance/counts"))).GetProperty("data");
        Assert.Equal(1, counts.GetProperty("due").GetInt32() + counts.GetProperty("overdue").GetInt32());

        var usage = (await Root(await h.Client.GetAsync("/api/v1/mold-maintenance/mold-usage"))).GetProperty("data")[0];
        Assert.Equal("MPMD-0001", usage.GetProperty("openPmNo").GetString());
        Assert.Equal(600_000, usage.GetProperty("nextThresholdShots").GetInt64());

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/mold-maintenance/1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.GetAsync("/api/v1/mold-maintenance/99")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.GetAsync("/api/v1/mold-maintenance?bucket=pending")).StatusCode);
        Assert.All(h.Authorization.Checks, c => Assert.Equal((ModuleCodes.TrnMoldPm, PermissionAction.View), (c.ModuleCode, c.Action)));
    }

    [Fact]
    public async Task NoManualScheduling_PostIsNotAllowed()
    {
        var h = CreateHarness();
        var r = await h.Client.PostAsync("/api/v1/mold-maintenance", Json("{\"moldId\":10}"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, r.StatusCode);
        Assert.Single(h.Pms.Pms);
    }

    [Fact]
    public async Task WithoutView_403_WithoutToken_401()
    {
        var h = CreateHarness(decide: (_, _, _) => false);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/mold-maintenance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/mold-maintenance/mold-usage")).StatusCode);

        var anon = CreateHarness(authenticate: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Client.GetAsync("/api/v1/mold-maintenance")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Client.GetAsync("/api/v1/notifications")).StatusCode);
    }

    [Fact]
    public async Task StartAndComplete_NeedEdit_ViewAloneIs403_AndNothingChanges()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.TrnMoldPm && action == PermissionAction.View);
        var rv = Convert.ToBase64String(h.Pms.Pms[0].RowVersion);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/mold-maintenance/1/start", Json($"{{\"rowVersion\":\"{rv}\"}}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/mold-maintenance/1/complete", Json($"{{\"maintenanceBy\":\"Ravi\",\"rowVersion\":\"{rv}\"}}"))).StatusCode);
        Assert.Equal(MoldPmStatus.Scheduled, h.Pms.Pms[0].Status);
    }

    [Fact]
    public async Task Complete_WithEdit_200_ThenStale409_Validation400()
    {
        var h = CreateHarness();
        var rv = Convert.ToBase64String(h.Pms.Pms[0].RowVersion);

        var bad = await h.Client.PutAsync("/api/v1/mold-maintenance/1/complete", Json($"{{\"maintenanceBy\":\" \",\"rowVersion\":\"{rv}\"}}"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await h.Client.PutAsync("/api/v1/mold-maintenance/1/complete", Json($"{{\"maintenanceBy\":\"Ravi\",\"remarks\":\"done\",\"rowVersion\":\"{rv}\"}}"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var data = (await Root(ok)).GetProperty("data");
        Assert.Equal(("Completed", 605_000), (data.GetProperty("status").GetString(), data.GetProperty("usageAtCompletion").GetInt32()));

        var again = await h.Client.PutAsync("/api/v1/mold-maintenance/1/complete", Json($"{{\"maintenanceBy\":\"Ravi\",\"rowVersion\":\"{rv}\"}}"));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Notifications_OnlyThoseOfModulesTheUserCanView()
    {
        var h = CreateHarness(permissions: new StubPermissionService((ModuleCodes.TrnMoldPm, true), (ModuleCodes.TrnMachinePm, false)));
        var items = (await Root(await h.Client.GetAsync("/api/v1/notifications"))).GetProperty("data").GetProperty("items");

        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("Mold PM due: MLD-0010", items[0].GetProperty("title").GetString());

        var none = CreateHarness(permissions: new StubPermissionService((ModuleCodes.TrnMoldPm, false)));
        var empty = (await Root(await none.Client.GetAsync("/api/v1/notifications"))).GetProperty("data").GetProperty("items");
        Assert.Equal(0, empty.GetArrayLength());
    }
}
