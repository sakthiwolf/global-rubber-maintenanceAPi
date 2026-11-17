namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/molds - the fields of the Add Mold form (analysis 4.2 / F-03). Not accepted (and
/// ignored by model binding if sent): moldId, moldCode (issued from the MOLD document sequence), currentUsageShots (a new
/// mold starts at 0, or at its opening usage when it was already in production; afterwards it is corrected on edit only -
/// BR-14), lifeState (computed), rowVersion and the audit columns.
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

    /// <summary>F01 fields (migration 024) - optional; trimmed, blank = null. Max 100 / 250 / 100 characters.</summary>
    public string? PartNo { get; init; }
    public string? PartDescription { get; init; }
    public string? Ownership { get; init; }
    public DateOnly? CommissionDate { get; init; }

    /// <summary>Required. Max &gt; 0; Warning &lt; Max; Replacement &gt; Warning and &lt;= Max (the CK constraints).</summary>
    public int? MaximumShots { get; init; }
    public int? WarningShots { get; init; }
    public int? ReplacementShots { get; init; }

    /// <summary>
    /// The usage-based PM interval in shots (migration 014). Optional: blank = usage-based PM disabled; otherwise &gt; 0.
    /// The form defaults it to 50,000.
    /// </summary>
    public int? MaintenanceFrequencyShots { get; init; }

    /// <summary>
    /// PM warning margin in REMAINING shots (migration 014): the warning is raised when 0 &lt; remaining &lt;= this.
    /// Optional; when entered it needs an interval and must be &gt; 0 and below it.
    /// </summary>
    public int? PmWarningShots { get; init; }

    public int? ResponsibleEmployeeId { get; init; }

    /// <summary>Required: one of the five CK values. The form defaults it to Available (template: user choice, Q-09).</summary>
    public string? Status { get; init; }

    public string? Remarks { get; init; }

    /// <summary>
    /// Opening balance - CREATE ONLY (ignored on update, where usage is corrected through CurrentUsageShots). For a mold
    /// that was already in production before it was registered here: the shots it has already done. Blank = a new mold
    /// starting at 0. Must be &gt;= 0; needs <see cref="OpeningBalanceDate"/>.
    /// </summary>
    public int? OpeningUsageShots { get; init; }

    /// <summary>
    /// Opening balance - CREATE ONLY. The mold's usage when its last preventive maintenance was done; it becomes the PM
    /// cycle start (next PM = this + Maintenance Frequency). Optional; needs an opening usage and a maintenance frequency,
    /// and cannot be above the opening usage. Blank = the cycle starts at the last whole interval below the opening usage.
    /// </summary>
    public int? UsageAtLastPmShots { get; init; }

    /// <summary>Opening balance - CREATE ONLY. The date the opening figures were taken: required with an opening usage, not in the future.</summary>
    public DateOnly? OpeningBalanceDate { get; init; }
}
