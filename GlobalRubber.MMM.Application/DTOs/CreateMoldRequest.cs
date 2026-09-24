namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/molds - the fields of the Add Mold form (analysis 4.2 / F-03). Not accepted (and
/// ignored by model binding if sent): moldId, moldCode (issued from the MOLD document sequence), currentUsageShots (a new
/// mold starts at 0; it is corrected on edit only - BR-14), lifeState (computed), rowVersion and the audit columns.
/// </summary>
public class CreateMoldRequest
{
    public string MoldName { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public string MoldType { get; init; } = string.Empty;

    /// <summary>Optional; omitted means the template/database default of 1. Must be &gt;= 1 (CK_mold_master_cavity_count).</summary>
    public int? CavityCount { get; init; }

    public string? Manufacturer { get; init; }
    public string? SerialNumber { get; init; }
    public string? Location { get; init; }
    public string? StorageLocation { get; init; }
    public DateOnly? CommissionDate { get; init; }

    /// <summary>Required. Max &gt; 0; Warning &lt; Max; Replacement &gt; Warning and &lt;= Max (the CK constraints).</summary>
    public int? MaximumShots { get; init; }
    public int? WarningShots { get; init; }
    public int? ReplacementShots { get; init; }

    /// <summary>Optional (nullable column). The form defaults it to 50,000.</summary>
    public int? MaintenanceFrequencyShots { get; init; }

    public int? ResponsibleEmployeeId { get; init; }

    /// <summary>Required: one of the five CK values. The form defaults it to Available (template: user choice, Q-09).</summary>
    public string? Status { get; init; }

    public string? Remarks { get; init; }
}
