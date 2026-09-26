using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// The production side of the mold-life engine, exactly as the analysis defines it (8.2, 8.3, BR-10 to BR-12) and the
/// template implements it (productionService.ts). Open questions are kept as the template behaves, not resolved:
/// usage counts production pieces 1:1 and ignores cavities (Q-01); only the usage BEFORE the entry is checked, so one
/// entry may overshoot the limit (Q-13); molds in any status, including Retired and Maintenance, may produce (Q-09).
/// </summary>
public static class MoldLifeEngine
{
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
