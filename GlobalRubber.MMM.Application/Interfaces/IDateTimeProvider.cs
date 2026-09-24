namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Abstraction over the current date/time so application code never calls
/// <see cref="DateTime.Now"/> or <see cref="DateTime.UtcNow"/> directly - that would make it
/// untestable and, per the system analysis (D-01), it is easy to get the business date wrong
/// (the template computes "today" in UTC, which is wrong for part of every IST day).
/// The concrete implementation lives in the Infrastructure project.
/// </summary>
public interface IDateTimeProvider
{
    /// <summary>Current instant in UTC. Use for timestamps that are stored and compared as UTC.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// The current business date (no time component) in the plant's local time zone.
    /// Future modules use this - not <see cref="DateTime.Today"/> - for "today" comparisons
    /// such as PM due/overdue buckets.
    /// </summary>
    DateOnly Today { get; }
}
