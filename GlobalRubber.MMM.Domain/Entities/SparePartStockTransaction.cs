namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.spare_part_stock_transaction (migration 016) - the stock ledger: one row per change of a spare
/// part's current stock (Opening / Issue / Reversal / Adjustment), with the signed quantity, the stock before and after
/// (CK: new = previous + quantity, new &gt;= 0) and what caused it. Insert-only.
/// </summary>
public class SparePartStockTransaction
{
    public long StockTransactionId { get; set; }
    public int SparePartId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int PreviousStock { get; set; }
    public int NewStock { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceNo { get; set; }
    public DateTime TransactionAt { get; set; }
    public int? CreatedBy { get; set; }
    public string? Remarks { get; set; }
}
