using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// Spare-part stock rules. The status mirrors the persisted computed column stock_status exactly (BR-29): current &lt;= 0
/// Out of Stock; current &lt;= minimum Low Stock; otherwise Available. Notifications fire only on a TRANSITION into Low Stock
/// or into Out of Stock (10 -&gt; 8 -&gt; 6 -&gt; 4 with minimum 5 notifies once, at 4).
/// </summary>
public static class SparePartStockRules
{
    public static string StatusOf(int currentStock, int minimumStock) =>
        currentStock <= 0 ? SparePartStockStatus.OutOfStock
        : currentStock <= minimumStock ? SparePartStockStatus.LowStock
        : SparePartStockStatus.Available;

    /// <summary>An issue is allowed only when it leaves the stock at 0 or above (never negative).</summary>
    public static bool CanIssue(int currentStock, int quantity) => quantity > 0 && currentStock >= quantity;

    /// <summary>The status the stock moved INTO, when it moved into Low Stock or Out of Stock; otherwise null.</summary>
    public static string? AlertTransition(int stockBefore, int stockAfter, int minimumStock)
    {
        var before = StatusOf(stockBefore, minimumStock);
        var after = StatusOf(stockAfter, minimumStock);
        if (before == after) return null;
        return after is SparePartStockStatus.LowStock or SparePartStockStatus.OutOfStock ? after : null;
    }
}
