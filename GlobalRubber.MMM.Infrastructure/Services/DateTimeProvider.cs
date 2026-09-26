using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IDateTimeProvider"/> implementation, backed by the system clock. "Today" is the plant's date
/// (India Standard Time - see <see cref="PlantTime"/>).
/// </summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    public static readonly TimeSpan PlantUtcOffset = PlantTime.UtcOffset;

    public DateTime UtcNow => DateTime.UtcNow;

    public DateOnly Today => PlantDate(DateTime.UtcNow);

    /// <summary>The plant's calendar date at the given UTC instant (e.g. 2026-09-24 20:00 UTC is 2026-09-25 in IST).</summary>
    public static DateOnly PlantDate(DateTime utcNow) => PlantTime.ToPlantDate(utcNow);
}
