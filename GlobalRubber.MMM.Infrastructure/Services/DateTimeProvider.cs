using GlobalRubber.MMM.Application.Interfaces;

namespace GlobalRubber.MMM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IDateTimeProvider"/> implementation, backed by the system clock.
/// </summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    // NOTE: once the plant's IST offset is confirmed (see system analysis Q-39), this should
    // convert UtcNow into IST before taking the date, rather than using UTC directly.
}
