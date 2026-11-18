using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// The production side of the mold-life engine, exactly as the analysis defines it (8.2, 8.3, BR-10 to BR-12) and the
/// template implements it (productionService.ts). Q-01 is resolved (approved 2026-11-17): mold usage is counted in
/// SHOTS - the pieces produced divided by the mold's cavities, rounded up - since one shot makes one piece per cavity
/// (an LH/RH tool makes both parts in one shot). Still open and kept as the template behaves: only the usage BEFORE the
/// entry is checked, so one entry may overshoot the limit (Q-13); molds in any status may produce.
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
    /// (a Retired mold stays Retired); usage after &lt; warning -&gt; an Available mold becomes In Production (approved
    /// 2026-11-18, A3 - it is now in use; was Q-10 "unchanged"), any other status is unchanged.
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

        return currentStatus == MoldStatus.Available ? MoldStatus.InProduction : currentStatus;
    }

    /// <summary>
    /// Approved 2026-11-18 (A2): the mold status is system-managed - the only manual choice is Retire / Reactivate.
    /// On a Mold master save:
    ///   Retired requested                        -&gt; Retired;
    ///   currently Retired, nothing else requested -&gt; stays Retired (reactivation must be asked for explicitly);
    ///   currently Maintenance (a PM in progress)  -&gt; stays Maintenance (the PM's completion releases it);
    ///   otherwise from the usage: &gt;= replacement -&gt; Replacement Due; &gt; 0 -&gt; In Production; 0 -&gt; Available.
    /// <paramref name="currentStatus"/> is null on create.
    /// </summary>
    public static string StatusAfterMasterSave(string? currentStatus, string? requestedStatus, int usageShots, int replacementShots)
    {
        if (requestedStatus == MoldStatus.Retired)
        {
            return MoldStatus.Retired;
        }

        if (currentStatus == MoldStatus.Retired && requestedStatus is null)
        {
            return MoldStatus.Retired;
        }

        if (currentStatus == MoldStatus.Maintenance)
        {
            return MoldStatus.Maintenance;
        }

        return StatusFromUsage(usageShots, replacementShots);
    }

    /// <summary>
    /// Approved 2026-11-18 (A4): the mold status after a production entry is CANCELLED and its shots are taken back off.
    /// Retired and Maintenance stay as they are; otherwise the status follows the new usage (StatusFromUsage), so a mold
    /// that reached Replacement Due only through the wrong entry becomes usable again.
    /// </summary>
    public static string StatusAfterCancellation(string currentStatus, int usageAfterCancel, int replacementShots) =>
        currentStatus is MoldStatus.Retired or MoldStatus.Maintenance ? currentStatus : StatusFromUsage(usageAfterCancel, replacementShots);

    /// <summary>The usage-derived status (A2): &gt;= replacement -&gt; Replacement Due; &gt; 0 -&gt; In Production; else Available.</summary>
    public static string StatusFromUsage(int usageShots, int replacementShots) =>
        usageShots >= replacementShots ? MoldStatus.ReplacementDue
        : usageShots > 0 ? MoldStatus.InProduction
        : MoldStatus.Available;
}
