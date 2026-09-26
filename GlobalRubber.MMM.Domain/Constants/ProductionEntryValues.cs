namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// transactions.production_entry_transaction.shift values, exactly as CK_production_entry_transaction_shift allows them
/// (analysis 6.1: fixed list, default Shift A - shift timings are open question Q-29).
/// </summary>
public static class ProductionShift
{
    public const string ShiftA = "Shift A";
    public const string ShiftB = "Shift B";
    public const string ShiftC = "Shift C";

    public static readonly IReadOnlyList<string> All = new[] { ShiftA, ShiftB, ShiftC };
}

/// <summary>
/// transactions.production_entry_transaction.status values (CK_production_entry_transaction_status, default 'Saved').
/// Only Saved is ever written: the analysis has no cancel action and the "Cancelled" value is unused (Q-14).
/// </summary>
public static class ProductionEntryStatus
{
    public const string Saved = "Saved";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = new[] { Saved, Cancelled };
}
