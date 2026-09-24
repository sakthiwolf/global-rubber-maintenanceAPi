namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Thrown when a request fails input validation. The global exception handler maps this to
/// HTTP 400 and returns <see cref="Errors"/> in the standard <c>ApiResponse.Errors</c> list.
/// This is a generic, reusable type - it carries no knowledge of any specific module.
/// </summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException()
        : base("One or more validation errors occurred.")
    {
        Errors = Array.Empty<string>();
    }

    public ValidationException(IEnumerable<string> errors)
        : this()
    {
        Errors = errors.ToArray();
    }

    public ValidationException(string error)
        : this(new[] { error })
    {
    }
}
