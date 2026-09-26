namespace GlobalRubber.MMM.Api.Configuration;

/// <summary>Bound from the <c>Swagger</c> configuration section.</summary>
public sealed class SwaggerOptions
{
    public const string SectionName = "Swagger";

    public string Title { get; init; } = "Global Rubber - Machine & Mold Maintenance API";
    public string Version { get; init; } = "v1";
    public string Description { get; init; } = string.Empty;
    public string ContactName { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;

    /// <summary>
    /// Serve Swagger UI / swagger.json outside Development too (e.g. production). Always on in Development.
    /// Every API endpoint still requires its JWT + permission - Swagger only documents them.
    /// </summary>
    public bool Enabled { get; init; }
}
