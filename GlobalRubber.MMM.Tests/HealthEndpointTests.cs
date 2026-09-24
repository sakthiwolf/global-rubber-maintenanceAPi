using System.Net;
using System.Text.Json;

namespace GlobalRubber.MMM.Tests;

public class HealthEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Liveness_AlwaysReportsHealthy_RegardlessOfDatabaseAvailability()
    {
        // /liveness only runs the "self" check, which never touches the database, so it must
        // always succeed even when SQL Server is unreachable in this environment.
        var response = await _client.GetAsync("/liveness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/readiness")]
    public async Task Endpoint_RespondsWithWellFormedHealthReport(string path)
    {
        // These endpoints include the database check. In an environment with no SQL Server
        // reachable, an Unhealthy (503) result is the *correct* answer - we only assert the
        // endpoint is reachable and returns a well-formed report, not that the DB is up.
        var response = await _client.GetAsync(path);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503 from {path}, got {(int)response.StatusCode}.");

        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("status", out _));
        Assert.True(body.TryGetProperty("checks", out var checks));
        Assert.True(checks.TryGetProperty("database", out _));
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
