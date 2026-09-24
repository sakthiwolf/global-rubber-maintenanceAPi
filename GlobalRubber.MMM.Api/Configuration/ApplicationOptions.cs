namespace GlobalRubber.MMM.Api.Configuration;

/// <summary>
/// Bound from the <c>Application</c> configuration section. Generic, presentation-level
/// metadata about this deployment - not to be confused with the Application *project*.
/// </summary>
public sealed class ApplicationOptions
{
    public const string SectionName = "Application";

    public string Name { get; init; } = "Global Rubber MMM API";
    public string Phase { get; init; } = "Backend Foundation";
}
