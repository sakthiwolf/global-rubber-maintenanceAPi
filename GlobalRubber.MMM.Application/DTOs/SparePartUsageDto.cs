using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>A spare part usage as returned by GET /api/v1/spare-part-usage (and its details).</summary>
public sealed class SparePartUsageDto
{
    public int SparePartUsageId { get; init; }
    public string UsageNo { get; init; } = string.Empty;
    public DateOnly UsageDate { get; init; }
    public int SparePartId { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;

    /// <summary>The unit of measure - from the Spare Part master, never entered on the usage.</summary>
    public string Unit { get; init; } = string.Empty;

    public int Quantity { get; init; }

    /// <summary>Machine PM / Mold PM.</summary>
    public string MaintenanceType { get; init; } = string.Empty;

    public int? MachinePmId { get; init; }
    public int? MoldPmId { get; init; }

    /// <summary>The maintenance record's number (MPM-... / MPMD-...).</summary>
    public string MaintenanceNo { get; init; } = string.Empty;

    public int? MachineId { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public int? MoldId { get; init; }
    public string? MoldCode { get; init; }
    public string? MoldName { get; init; }
    public int? UsedByEmployeeId { get; init; }
    public string? UsedByEmployeeName { get; init; }
    public decimal? UnitCostAtIssue { get; init; }

    /// <summary>Quantity x unit cost at issue (null when the part had no unit cost).</summary>
    public decimal? UsageCost { get; init; }

    public string? Remarks { get; init; }

    /// <summary>Posted / Reversed.</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime? ReversedAt { get; init; }
    public int? ReversedBy { get; init; }
    public string? ReversalReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>The ledger rows this usage caused (the Issue and, once reversed, the Reversal) - details only.</summary>
    public IReadOnlyList<SparePartStockMovementDto> StockMovements { get; init; } = Array.Empty<SparePartStockMovementDto>();

    /// <summary>Opaque base64 concurrency token - echo it back on reverse.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>One stock ledger row.</summary>
public sealed class SparePartStockMovementDto
{
    public long StockTransactionId { get; init; }
    public string TransactionType { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int PreviousStock { get; init; }
    public int NewStock { get; init; }
    public string? ReferenceNo { get; init; }
    public DateTime TransactionAt { get; init; }
    public int? CreatedBy { get; init; }
    public string? Remarks { get; init; }
}

/// <summary>GET /api/v1/spare-part-usage query - server-side paging and filters.</summary>
public sealed class SparePartUsageListQuery : PaginationRequest
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int? SparePartId { get; set; }
    public int? MachineId { get; set; }
    public int? MoldId { get; set; }

    /// <summary>Machine PM / Mold PM; any other value is a 400.</summary>
    public string? MaintenanceType { get; set; }

    /// <summary>Posted / Reversed; any other value is a 400.</summary>
    public string? Status { get; set; }

    /// <summary>Case-insensitive "contains" match on the usage number or the maintenance number.</summary>
    public string? Search { get; set; }
}

/// <summary>Body of POST /api/v1/spare-part-usage.</summary>
public sealed class CreateSparePartUsageRequest
{
    /// <summary>Machine PM / Mold PM.</summary>
    public string? MaintenanceType { get; init; }

    /// <summary>The Machine PM id or Mold PM id (verified by the server: exists, open and due).</summary>
    public int? MaintenanceId { get; init; }

    public int? SparePartId { get; init; }

    /// <summary>Whole units, &gt; 0 and no more than the available stock (stock is stored as a whole number).</summary>
    public decimal? Quantity { get; init; }

    /// <summary>Required (the form defaults it to today's plant date).</summary>
    public DateOnly? UsageDate { get; init; }

    /// <summary>Optional active employee.</summary>
    public int? UsedByEmployeeId { get; init; }

    /// <summary>Optional, at most 500.</summary>
    public string? Remarks { get; init; }

    /// <summary>
    /// Idempotency key generated once per form: a retried / double-submitted request with the same key returns the
    /// usage that was already posted instead of issuing the part twice.
    /// </summary>
    public Guid? RequestId { get; init; }
}

/// <summary>Body of DELETE /api/v1/spare-part-usage/{id} - a REVERSAL, never a physical delete.</summary>
public sealed class ReverseSparePartUsageRequest
{
    /// <summary>Required, at most 500 - why the usage is reversed.</summary>
    public string? Reason { get; init; }

    public string? RowVersion { get; init; }
}

/// <summary>GET /api/v1/spare-part-usage/lookups - what the Add form needs, small by construction.</summary>
public sealed class SparePartUsageLookupsDto
{
    /// <summary>Active spare parts with their unit and current stock.</summary>
    public IReadOnlyList<UsageSparePartLookupDto> SpareParts { get; init; } = Array.Empty<UsageSparePartLookupDto>();

    /// <summary>Open Machine PMs that are due (scheduled on or before today's IST date).</summary>
    public IReadOnlyList<UsageMaintenanceLookupDto> MachinePms { get; init; } = Array.Empty<UsageMaintenanceLookupDto>();

    /// <summary>Open Mold PMs (Scheduled / In Progress).</summary>
    public IReadOnlyList<UsageMaintenanceLookupDto> MoldPms { get; init; } = Array.Empty<UsageMaintenanceLookupDto>();

    /// <summary>Active employees for "Used By".</summary>
    public IReadOnlyList<UsageEmployeeLookupDto> Employees { get; init; } = Array.Empty<UsageEmployeeLookupDto>();
}

public sealed class UsageSparePartLookupDto
{
    public int SparePartId { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public int CurrentStock { get; init; }
    public int MinimumStock { get; init; }
    public string StockStatus { get; init; } = string.Empty;
}

public sealed class UsageMaintenanceLookupDto
{
    public int MaintenanceId { get; init; }
    public string MaintenanceNo { get; init; } = string.Empty;

    /// <summary>MAC-... / MLD-... and name.</summary>
    public string AssetCode { get; init; } = string.Empty;
    public string AssetName { get; init; } = string.Empty;
    public DateOnly ScheduledDate { get; init; }
    public string Status { get; init; } = string.Empty;
}

public sealed class UsageEmployeeLookupDto
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
}

/// <summary>The maintenance record a usage is issued against, as read (and locked) inside the save.</summary>
public sealed record MaintenanceReference(
    int MaintenanceId, string MaintenanceNo, string Status, DateOnly ScheduledDate, int? MachineId, int? MoldId, string AssetCode);
