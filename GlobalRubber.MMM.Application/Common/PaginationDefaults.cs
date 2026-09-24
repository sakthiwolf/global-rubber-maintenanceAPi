namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Default and boundary values shared by every paginated endpoint, so individual modules never
/// have to redeclare or accidentally diverge on these numbers.
/// </summary>
public static class PaginationDefaults
{
    public const int DefaultPageNumber = 1;
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;
}
