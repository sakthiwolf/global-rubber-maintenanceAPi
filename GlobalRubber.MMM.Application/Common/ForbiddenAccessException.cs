namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Thrown when an authenticated user is not permitted to perform the requested action. The
/// global exception handler maps this to HTTP 403.
///
/// Plain HTTP 401 (Unauthorized - no/invalid credentials) does not need a matching exception
/// type here: it is produced by the authentication middleware itself before a request ever
/// reaches application code, once authentication is added in a later phase.
/// </summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.")
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }
}
