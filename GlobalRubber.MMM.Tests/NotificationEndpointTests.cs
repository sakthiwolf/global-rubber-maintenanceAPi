using System.Net;
using System.Text.Json;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// HTTP contract of the Notification Module (/api/v1/notifications, migration 018 read state) through the real pipeline
/// (JWT, ApiResponse, GlobalExceptionHandler) with an in-memory repository. The user may View TRN_MOLD_PM and
/// MASTER_SPARE_PART but not TRN_MACHINE_PM: notification 2 belongs to TRN_MACHINE_PM and must stay invisible.
/// </summary>
public class NotificationEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public NotificationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed class StubPermissionService : IPermissionService
    {
        private readonly IReadOnlyList<ModulePermissionDto> _mine;
        public StubPermissionService(params (string Module, bool View)[] modules) =>
            _mine = modules.Select(m => new ModulePermissionDto { ModuleCode = m.Module, CanView = m.View }).ToList();
        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest r, int? u, string? ip, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken c) => Task.FromResult(new RolePermissionMatrixDto { Permissions = _mine });
    }

    private sealed record H(WebApplicationFactory<Program> Factory, HttpClient Client, InMemoryNotificationRepository Notifications);

    private H CreateHarness(bool authenticate = true, int userId = 1, InMemoryNotificationRepository? shared = null, StubPermissionService? permissions = null)
    {
        var notifications = shared ?? Seed();
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(notifications);
            services.RemoveAll<IPermissionService>();
            services.AddSingleton<IPermissionService>(permissions ?? new StubPermissionService(
                (ModuleCodes.TrnMoldPm, true), (ModuleCodes.MasterSparePart, true), (ModuleCodes.TrnMachinePm, false)));
        }));
        var client = factory.CreateClient();
        if (authenticate) TestAuth.Authenticate(client, factory, "ADMIN", userId);
        return new H(factory, client, notifications);
    }

    private static InMemoryNotificationRepository Seed()
    {
        var repo = new InMemoryNotificationRepository();
        var t0 = new DateTime(2026, 9, 25, 6, 0, 0, DateTimeKind.Utc);
        repo.Items.Add(new Notification { NotificationId = 1, NotificationType = NotificationTypes.MoldPmDue, ModuleCode = ModuleCodes.TrnMoldPm, Severity = "Critical", Title = "Mold PM due: MLD-0010", Message = "m1", RecordRef = "MLD-0010", LinkPath = "/transactions/mold-maintenance", EventKey = "k1", CreatedAt = t0 });
        repo.Items.Add(new Notification { NotificationId = 2, NotificationType = NotificationTypes.MoldPmWarning, ModuleCode = ModuleCodes.TrnMachinePm, Severity = "Warning", Title = "Other module", Message = "m2", EventKey = "k2", CreatedAt = t0.AddMinutes(1) });
        repo.Items.Add(new Notification { NotificationId = 3, NotificationType = NotificationTypes.SparePartLowStock, ModuleCode = ModuleCodes.MasterSparePart, Severity = "Warning", Title = "Spare part SPR-0001 is low on stock", Message = "m3", EventKey = "k3", CreatedAt = t0.AddMinutes(2) });
        return repo;
    }

    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<int> Unread(HttpClient c) => (await Root(await c.GetAsync("/api/v1/notifications/unread-count"))).GetProperty("data").GetProperty("unreadCount").GetInt32();
    private static HttpRequestMessage Patch(string url) => new(HttpMethod.Patch, url);

    [Fact]
    public async Task EveryEndpoint_WithoutToken_Is401()
    {
        var h = CreateHarness(authenticate: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/notifications/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.SendAsync(Patch("/api/v1/notifications/1/read"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.SendAsync(Patch("/api/v1/notifications/read-all"))).StatusCode);
        Assert.Empty(h.Notifications.Reads);
    }

    [Fact]
    public async Task List_IsNewestFirst_OnlyViewableModules_WithReadState()
    {
        var h = CreateHarness();
        var data = (await Root(await h.Client.GetAsync("/api/v1/notifications?pageNumber=1&pageSize=10"))).GetProperty("data");
        var items = data.GetProperty("items");

        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { 3, 1 }, items.EnumerateArray().Select(i => i.GetProperty("notificationId").GetInt32()));
        Assert.All(items.EnumerateArray(), i => Assert.False(i.GetProperty("isRead").GetBoolean()));
        Assert.Equal("/transactions/mold-maintenance", items[1].GetProperty("linkPath").GetString());
        Assert.Equal(2, await Unread(h.Client));

        var paged = (await Root(await h.Client.GetAsync("/api/v1/notifications?pageNumber=2&pageSize=1"))).GetProperty("data");
        Assert.Equal(1, paged.GetProperty("items")[0].GetProperty("notificationId").GetInt32());
        Assert.Equal(2, paged.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task MarkRead_ThenIdempotent_AndUnreadOnlyFilter()
    {
        var h = CreateHarness();

        var first = await h.Client.SendAsync(Patch("/api/v1/notifications/1/read"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var dto = (await Root(first)).GetProperty("data");
        Assert.True(dto.GetProperty("isRead").GetBoolean());
        var readAt = dto.GetProperty("readAt").GetString();
        Assert.Equal(1, await Unread(h.Client));

        var again = (await Root(await h.Client.SendAsync(Patch("/api/v1/notifications/1/read")))).GetProperty("data");
        Assert.Equal(readAt, again.GetProperty("readAt").GetString()); // the original read time is kept
        Assert.Single(h.Notifications.Reads);

        var unreadOnly = (await Root(await h.Client.GetAsync("/api/v1/notifications?unreadOnly=true"))).GetProperty("data").GetProperty("items");
        Assert.Equal(new[] { 3 }, unreadOnly.EnumerateArray().Select(i => i.GetProperty("notificationId").GetInt32()));
    }

    [Fact]
    public async Task AnotherModulesNotification_OrAnUnknownId_Is404_AndNothingIsWritten()
    {
        var h = CreateHarness();

        var other = await h.Client.SendAsync(Patch("/api/v1/notifications/2/read"));
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.False((await Root(other)).GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.SendAsync(Patch("/api/v1/notifications/999/read"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.SendAsync(Patch("/api/v1/notifications/abc/read"))).StatusCode); // route constraint
        Assert.Empty(h.Notifications.Reads);
    }

    [Fact]
    public async Task ReadState_IsPerUser()
    {
        var shared = Seed();
        var user1 = CreateHarness(userId: 1, shared: shared);
        var user2 = CreateHarness(userId: 2, shared: shared);

        await user1.Client.SendAsync(Patch("/api/v1/notifications/read-all"));

        Assert.Equal(0, await Unread(user1.Client));
        Assert.Equal(2, await Unread(user2.Client)); // user 2 has read nothing
        Assert.All(shared.Reads, r => Assert.Equal(1, r.UserId));
    }

    [Fact]
    public async Task MarkAll_MarksOnlyTheUsersViewableUnread()
    {
        var h = CreateHarness();
        await h.Client.SendAsync(Patch("/api/v1/notifications/3/read"));

        var result = (await Root(await h.Client.SendAsync(Patch("/api/v1/notifications/read-all")))).GetProperty("data");

        Assert.Equal(1, result.GetProperty("markedCount").GetInt32());
        Assert.Equal(0, result.GetProperty("unreadCount").GetInt32());
        Assert.DoesNotContain(h.Notifications.Reads, r => r.NotificationId == 2); // not the invisible module's
        Assert.Equal(0, (await Root(await h.Client.SendAsync(Patch("/api/v1/notifications/read-all")))).GetProperty("data").GetProperty("markedCount").GetInt32());
    }

    [Fact]
    public async Task AUserWhoCanViewNothing_SeesNothing_AndCannotMark()
    {
        var h = CreateHarness(permissions: new StubPermissionService((ModuleCodes.TrnMoldPm, false)));
        Assert.Equal(0, (await Root(await h.Client.GetAsync("/api/v1/notifications"))).GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, await Unread(h.Client));
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.SendAsync(Patch("/api/v1/notifications/1/read"))).StatusCode);
        Assert.Equal(0, (await Root(await h.Client.SendAsync(Patch("/api/v1/notifications/read-all")))).GetProperty("data").GetProperty("markedCount").GetInt32());
        Assert.Empty(h.Notifications.Reads);
    }

    [Fact]
    public async Task ThereIsNoPublicCreateEndpoint()
    {
        var h = CreateHarness();
        var r = await h.Client.PostAsync("/api/v1/notifications", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, r.StatusCode);
        Assert.Equal(3, h.Notifications.Items.Count);
    }
}

/// <summary>INotificationPublisher: the single entry point business modules use to raise a notification.</summary>
public class NotificationPublisherTests
{
    private static (NotificationPublisher Publisher, InMemoryNotificationRepository Repo) Create()
    {
        var repo = new InMemoryNotificationRepository();
        return (new NotificationPublisher(repo, NullLogger<NotificationPublisher>.Instance), repo);
    }

    private static NewNotification Valid(string eventKey = "SPARE_PART_LOW:1:1") => new(
        NotificationTypes.SparePartLowStock, ModuleCodes.MasterSparePart, NotificationSeverity.Warning,
        "  Spare part SPR-0001 is low on stock ", "Only 2 left.", eventKey, "SparePart", 1, "SPR-0001", "/masters/spare-parts");

    [Fact]
    public async Task AValidNotification_IsWritten_Trimmed()
    {
        var (publisher, repo) = Create();
        Assert.Equal(NotificationWriteResult.Added, await publisher.PublishAsync(Valid(), CancellationToken.None));
        var stored = Assert.Single(repo.Items);
        Assert.Equal("Spare part SPR-0001 is low on stock", stored.Title);
        Assert.Null(stored.CreatedBy);
        Assert.True(await publisher.HasBeenPublishedAsync("SPARE_PART_LOW:1:1", CancellationToken.None));
    }

    [Fact]
    public async Task TheSameEvent_IsNotifiedOnce()
    {
        var (publisher, repo) = Create();
        await publisher.PublishAsync(Valid(), CancellationToken.None);
        Assert.Equal(NotificationWriteResult.AlreadyExists, await publisher.PublishAsync(Valid(), CancellationToken.None));
        Assert.Single(repo.Items);
    }

    public static IEnumerable<object[]> Invalid() => new[]
    {
        new object[] { Valid() with { NotificationType = "PmDue" } },         // not allowed by CK_notification_transaction_type
        new object[] { Valid() with { Severity = "High" } },                 // not allowed by CK_notification_transaction_severity
        new object[] { Valid() with { Title = " " } },
        new object[] { Valid() with { Message = new string('x', 501) } },
        new object[] { Valid() with { EventKey = "" } },
        new object[] { Valid() with { ModuleCode = "" } },
        new object[] { Valid() with { LinkPath = "https://evil.example/x" } }, // must be an app route
        new object[] { Valid() with { RecordRef = new string('x', 51) } },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task AnInvalidNotification_IsReportedFailed_NeverThrown_NothingWritten(NewNotification notification)
    {
        var (publisher, repo) = Create();
        Assert.Equal(NotificationWriteResult.Failed, await publisher.PublishAsync(notification, CancellationToken.None));
        Assert.Empty(repo.Items);
    }
}
