using System.Net;
using GlobalRubber.MMM.Api.Realtime;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The realtime notification signal (SignalR hub /hubs/notifications, replacing unread-count polling): what is pushed, to
/// whom, and only after a successful request. The hub context is replaced by a recorder, so no socket is needed.
/// </summary>
public class NotificationRealtimeTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public NotificationRealtimeTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ================================================================ recording hub context

    private sealed record Sent(string Target, string Method, int ArgCount);

    private sealed class RecordingHubContext : IHubContext<NotificationHub>, IHubClients
    {
        public List<Sent> Sends { get; } = new();
        public bool Throw { get; set; }
        public IHubClients Clients => this;
        public IGroupManager Groups => throw new NotSupportedException();

        private IClientProxy Proxy(string target) => new Recorder(this, target);
        public IClientProxy All => Proxy("all");
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy("users:" + string.Join(",", userIds));
        public IClientProxy User(string userId) => Proxy("users:" + userId);
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        private sealed class Recorder : IClientProxy
        {
            private readonly RecordingHubContext _owner;
            private readonly string _target;
            public Recorder(RecordingHubContext owner, string target) { _owner = owner; _target = target; }

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                if (_owner.Throw) throw new InvalidOperationException("hub down");
                _owner.Sends.Add(new Sent(_target, method, args.Length));
                return Task.CompletedTask;
            }
        }
    }

    private sealed class StubPermissionService : IPermissionService
    {
        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(int roleId, UpdateRolePermissionsRequest r, int? u, string? ip, CancellationToken c) => throw new NotSupportedException();
        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken c) =>
            Task.FromResult(new RolePermissionMatrixDto { Permissions = new[] { new ModulePermissionDto { ModuleCode = ModuleCodes.TrnMoldPm, CanView = true } } });
    }

    private sealed record H(WebApplicationFactory<Program> Factory, HttpClient Client, RecordingHubContext Hub);

    private H CreateHarness(bool authenticate = true)
    {
        var repo = new InMemoryNotificationRepository();
        repo.Items.Add(new Notification { NotificationId = 1, NotificationType = NotificationTypes.MoldPmDue, ModuleCode = ModuleCodes.TrnMoldPm, Severity = "Critical", Title = "t1", Message = "m1", EventKey = "k1", CreatedAt = DateTime.UtcNow });
        repo.Items.Add(new Notification { NotificationId = 2, NotificationType = NotificationTypes.MoldPmDue, ModuleCode = ModuleCodes.TrnMoldPm, Severity = "Critical", Title = "t2", Message = "m2", EventKey = "k2", CreatedAt = DateTime.UtcNow });
        var hub = new RecordingHubContext();
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(repo);
            services.RemoveAll<IPermissionService>();
            services.AddSingleton<IPermissionService>(new StubPermissionService());
            services.RemoveAll<IHubContext<NotificationHub>>();
            services.AddSingleton<IHubContext<NotificationHub>>(hub);
        }));
        var client = factory.CreateClient();
        if (authenticate) TestAuth.Authenticate(client, factory, "ADMIN", 7);
        return new H(factory, client, hub);
    }

    private static HttpRequestMessage Patch(string url) => new(HttpMethod.Patch, url);

    // ================================================================ read state -> only the user's own connections

    [Fact]
    public async Task MarkingOneRead_SignalsOnlyThatUser_WithNoPayload_AndAnIdempotentRepeatSignalsNothing()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(Patch("/api/v1/notifications/1/read"))).StatusCode);
        var sent = Assert.Single(h.Hub.Sends);
        Assert.Equal(new Sent("users:7", NotificationHub.NotificationsChangedMethod, 0), sent);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(Patch("/api/v1/notifications/1/read"))).StatusCode); // already read
        Assert.Single(h.Hub.Sends);
    }

    [Fact]
    public async Task MarkAll_SignalsTheUser_OnlyWhenSomethingWasMarked()
    {
        var h = CreateHarness();

        await h.Client.SendAsync(Patch("/api/v1/notifications/read-all"));
        Assert.Equal(new Sent("users:7", NotificationHub.NotificationsChangedMethod, 0), Assert.Single(h.Hub.Sends));

        await h.Client.SendAsync(Patch("/api/v1/notifications/read-all")); // nothing left to mark
        Assert.Single(h.Hub.Sends);
    }

    [Fact]
    public async Task FailedRequestsAndReads_SignalNothing()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.SendAsync(Patch("/api/v1/notifications/999/read"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/notifications/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/notifications")).StatusCode);

        Assert.Empty(h.Hub.Sends);
    }

    // ================================================================ the middleware: after success only

    private static async Task<RecordingHubContext> RunMiddleware(Func<HttpContext, NotificationChangeSignal, Task> endpoint, bool hubThrows = false)
    {
        var hub = new RecordingHubContext { Throw = hubThrows };
        var signal = new NotificationChangeSignal();
        var middleware = new NotificationSignalMiddleware(ctx => endpoint(ctx, signal), NullLogger<NotificationSignalMiddleware>.Instance);
        await middleware.InvokeAsync(new DefaultHttpContext(), signal, hub);
        return hub;
    }

    [Fact]
    public async Task ANewNotification_IsSignalledToEveryone_AfterTheRequestSucceeded()
    {
        var hub = await RunMiddleware((ctx, signal) =>
        {
            signal.NotificationsCreated();
            signal.ReadStateChanged(3); // covered by the broadcast - sent once
            ctx.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        Assert.Equal(new Sent("all", NotificationHub.NotificationsChangedMethod, 0), Assert.Single(hub.Sends));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    public async Task AFailedRequest_SignalsNothing(int status)
    {
        var hub = await RunMiddleware((ctx, signal) =>
        {
            signal.NotificationsCreated(); // written, but the request failed (its transaction rolled back)
            ctx.Response.StatusCode = status;
            return Task.CompletedTask;
        });

        Assert.Empty(hub.Sends);
    }

    [Fact]
    public async Task AnException_PropagatesAndSignalsNothing()
    {
        var hub = new RecordingHubContext();
        var signal = new NotificationChangeSignal();
        var middleware = new NotificationSignalMiddleware(_ =>
        {
            signal.NotificationsCreated();
            throw new InvalidOperationException("business failure");
        }, NullLogger<NotificationSignalMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(new DefaultHttpContext(), signal, hub));
        Assert.Empty(hub.Sends);
    }

    [Fact]
    public async Task APushFailure_NeverFailsTheRequest()
    {
        var hub = await RunMiddleware((_, signal) =>
        {
            signal.NotificationsCreated();
            return Task.CompletedTask;
        }, hubThrows: true);

        Assert.Empty(hub.Sends);
    }

    [Fact]
    public async Task NothingRecorded_NothingSent()
    {
        Assert.Empty((await RunMiddleware((_, _) => Task.CompletedTask)).Sends);
    }

    // ================================================================ the hub: signed-in users only

    private static HttpRequestMessage Negotiate(string? queryToken = null) =>
        new(HttpMethod.Post, "/hubs/notifications/negotiate?negotiateVersion=1" + (queryToken is null ? string.Empty : "&access_token=" + queryToken));

    [Fact]
    public async Task TheHub_RejectsAnonymousConnections()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.SendAsync(Negotiate())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.SendAsync(Negotiate("not-a-token"))).StatusCode);
    }

    [Fact]
    public async Task TheHub_AcceptsTheJwt_AsHeader_OrAsTheAccessTokenQueryValue()
    {
        var h = CreateHarness(authenticate: false);
        var token = TestAuth.MintToken(h.Factory, "ADMIN", 7);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(Negotiate(token))).StatusCode);
        var withHeader = Negotiate();
        withHeader.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.SendAsync(withHeader)).StatusCode);
    }

    [Fact]
    public async Task TheAccessTokenQueryValue_IsAcceptedOnlyOnTheHubPath()
    {
        var h = CreateHarness(authenticate: false);
        var token = TestAuth.MintToken(h.Factory, "ADMIN", 7);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/notifications/unread-count?access_token=" + token)).StatusCode);
    }
}
