namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Thrown when a requested record does not exist. The global exception handler maps this to
/// HTTP 404. Generic and reusable - future modules throw
/// <c>new NotFoundException(nameof(Machine), id)</c> without adding a new exception type.
/// </summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string entityName, object key)
        : base($"Entity \"{entityName}\" with key ({key}) was not found.")
    {
    }
}
