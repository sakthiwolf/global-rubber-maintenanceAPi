namespace GlobalRubber.MMM.Domain.Common;

/// <summary>
/// The plant's time zone: India Standard Time, UTC+05:30 (Global Rubber, Chennai - confirmed for Q-39, 2026-09-25).
/// India observes no daylight saving, so a fixed offset is exact and does not depend on the host's time-zone database.
/// Business dates ("today", a checklist's cycle anchor) are plant dates; stored timestamps stay UTC.
/// </summary>
public static class PlantTime
{
    public static readonly TimeSpan UtcOffset = TimeSpan.FromMinutes(330);

    /// <summary>The plant's calendar date at the given UTC instant (e.g. 2026-09-24 20:00 UTC is 2026-09-25 in IST).</summary>
    public static DateOnly ToPlantDate(DateTime utc) => DateOnly.FromDateTime(utc + UtcOffset);
}
