namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Production entry returned to API callers (transactions.production_entry_transaction) with the machine, product and
/// mold codes/names joined for display.
/// </summary>
public sealed class ProductionEntryDto
{
    public int ProductionEntryId { get; init; }
    public string EntryNo { get; init; } = string.Empty;
    public DateOnly EntryDate { get; init; }

    /// <summary>Shift A / Shift B / Shift C.</summary>
    public string Shift { get; init; } = string.Empty;

    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;

    public int ProductionQty { get; init; }
    public int RejectedQty { get; init; }

    /// <summary>production_qty - rejected_qty, computed by SQL Server (BR-09).</summary>
    public int GoodQty { get; init; }

    /// <summary>The mold's usage immediately before and after this entry (BR-11).</summary>
    public int MoldUsageBefore { get; init; }
    public int MoldUsageAfter { get; init; }

    public string? Remarks { get; init; }

    /// <summary>Saved (the only value ever written - Q-14).</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>Base64 row_version, returned like every other record. There is no update endpoint (analysis 6.1, Q-14).</summary>
    public string RowVersion { get; init; } = string.Empty;
}
