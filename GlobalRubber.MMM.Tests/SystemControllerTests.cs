using System.Net;
using System.Text.Json;

namespace GlobalRubber.MMM.Tests;

public class SystemControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemControllerTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Ping_Returns200_WithStandardApiResponseEnvelope()
    {
        var response = await _client.GetAsync("/api/v1/system/ping");

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Global Rubber MMM API is running.", root.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
    }
}

public class SwaggerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SwaggerTests(ApiWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerJson_IsAvailable_InDevelopment()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SwaggerUi_IsAvailable_InDevelopment()
    {
        var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
