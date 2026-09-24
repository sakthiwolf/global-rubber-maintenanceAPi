namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/spare-parts - the fields of the Spare Part form (analysis 4.7). Current stock is part of
/// it because the master owns stock entry (4.7: "Editable directly", Q-06). Not accepted (and ignored by model binding if
/// sent): sparePartId, sparePartCode (issued from the SPARE_PART document sequence), stockStatus (computed), isActive (a
/// new spare part is always active), rowVersion and the audit columns.
/// </summary>
public class CreateSparePartRequest
{
    public string SparePartName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public int? MachineId { get; init; }
    public string? PartNumber { get; init; }
    public string Unit { get; init; } = string.Empty;

    /// <summary>Required, &gt;= 0 (CK_spare_part_master_minimum_stock).</summary>
    public int? MinimumStock { get; init; }

    /// <summary>Required, &gt;= 0 (CK_spare_part_master_current_stock). 0 is allowed (the analysis fixes template defect D-04).</summary>
    public int? CurrentStock { get; init; }

    public int? VendorId { get; init; }
    public string? StoreLocation { get; init; }

    /// <summary>Optional; NULL or &gt;= 0 (CK_spare_part_master_unit_cost), DECIMAL(18,2).</summary>
    public decimal? UnitCost { get; init; }
}
