namespace GlobalRubber.MMM.Api.Configuration;

/// <summary>
/// Bound from the <c>Cors</c> configuration section. Never hard-code allowed origins in code -
/// they differ between Development, Staging and Production and must stay configurable.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>
    /// Origins allowed to call this API (e.g. the React dev server or its production URL).
    /// Deliberately never combined with AllowAnyOrigin - see section 20 of the system analysis.
    /// </summary>
    public string[] AllowedOrigins { get; init; } = Array.Empty<string>();
}
