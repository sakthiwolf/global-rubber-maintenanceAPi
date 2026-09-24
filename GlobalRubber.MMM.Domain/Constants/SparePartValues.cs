namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// masters.spare_part_master.stock_status values - the persisted computed column (BR-29): current &lt;= 0 Out of Stock;
/// current &lt;= minimum Low Stock; otherwise Available.
/// </summary>
public static class SparePartStockStatus
{
    public const string OutOfStock = "Out of Stock";
    public const string LowStock = "Low Stock";
    public const string Available = "Available";

    public static readonly IReadOnlyList<string> All = new[] { Available, LowStock, OutOfStock };
}
