using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// The production side of the mold-life engine, exactly as the analysis defines it (8.2, 8.3, BR-10 to BR-12) and the
/// template implements it (productionService.ts). Q-01 is resolved (approved 2026-11-17): mold usage is counted in
/// SHOTS - the pieces produced divided by the mold's cavities, rounded up - since one shot makes one piece per cavity
/// (an LH/RH tool makes both parts in one shot). Still open and kept as the template behaves: only the usage BEFORE the
/// entry is checked, so one entry may overshoot the limit (Q-13); molds in any status may produce (Q-09).
/// </summary>
public static class MoldLifeEngine
{
    /// <summary>
    /// Q-01: the shots an entry of <paramref name="pieces"/> took = pieces / cavities, rounded UP (a partly filled last
    /// shot is still a shot). A cavity count below 1 is treated as 1. E.g. 656 pieces on a 2-cavity tool = 328 shots;
    /// 100 pieces on a 3-cavity tool = 34 shots.
    /// </summary>
    public static int ShotsFromPieces(int pieces, int cavityCount)
    {
        if (pieces <= 0) return 0;
        var cavities = Math.Max(1, cavityCount);
        return (int)(((long)pieces + cavities - 1) / cavities);
    }

    /// <summary>BR-10: production is allowed while the mold's usage before the entry is below its replacement shots.</summary>
    public static bool IsProductionAllowed(int currentUsageShots, int replacementShots) =>
        currentUsageShots < replacementShots;

    /// <summary>
    /// BR-12 / 8.3: the mold status after a production save.
    /// usage after &gt;= replacement -&gt; Replacement Due; warning &lt;= usage after &lt; replacement -&gt; In Production
    /// (a Retired mold stays Retired); usage after &lt; warning -&gt; unchanged (Q-10).
    /// </summary>
    public static string StatusAfterProduction(string currentStatus, int usageAfter, int warningShots, int replacementShots)
    {
        if (usageAfter >= replacementShots)
        {
            return MoldStatus.ReplacementDue;
        }

        if (usageAfter >= warningShots)
        {
            return currentStatus == MoldStatus.Retired ? MoldStatus.Retired : MoldStatus.InProduction;
        }

        return currentStatus;
    }
}
