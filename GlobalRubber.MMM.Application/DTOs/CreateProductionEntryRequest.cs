namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/production-entries (analysis 6.1 / F-06 / 18.4). Not accepted (and ignored by model
/// binding if sent): productionEntryId, entryNo (issued from the PRODUCTION_ENTRY document sequence), goodQty (computed
/// by SQL Server), moldUsageBefore/After (taken from the locked mold row), status (always Saved), rowVersion and the audit
/// columns. Nullable members let a missing value be reported as "required" instead of silently becoming 0.
/// </summary>
public sealed class CreateProductionEntryRequest
{
    /// <summary>Required. No past/future limit is defined (Q-14).</summary>
    public DateOnly? EntryDate { get; init; }

    /// <summary>Required: Shift A / Shift B / Shift C (CK_production_entry_transaction_shift).</summary>
    public string? Shift { get; init; }

    /// <summary>Required; must be an active machine (BR-13).</summary>
    public int? MachineId { get; init; }

    /// <summary>Required; must be an active product (BR-13).</summary>
    public int? ProductId { get; init; }

    /// <summary>Required; must belong to the product, and its usage must be below its replacement shots (BR-10).</summary>
    public int? MoldId { get; init; }

    /// <summary>Required, &gt; 0 (BR-08).</summary>
    public int? ProductionQty { get; init; }

    /// <summary>Optional, default 0; 0 &lt;= rejected &lt;= production (BR-08).</summary>
    public int? RejectedQty { get; init; }

    /// <summary>Optional, at most 500 characters (NVARCHAR(500)).</summary>
    public string? Remarks { get; init; }
}
