namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Thrown when a request is well-formed but conflicts with the current state of the data - a
/// duplicate unique value, a record another user changed first, or an action the record's
/// current state forbids. The global exception handler maps this to HTTP 409. Its message is
/// written for the API caller: it never carries SQL text or constraint names.
/// </summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
