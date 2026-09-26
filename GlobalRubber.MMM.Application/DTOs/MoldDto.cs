namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Mold information returned to API callers (masters.mold_master joined to its product and responsible person), plus
/// the derived life values of analysis section 8.2 so pages never recompute them. There is no IsActive: Status
/// 'Retired' plays that role for molds.
/// </summary>
public sealed class MoldDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;

    /// <summary>Whether the mold's product is still active - lets a form keep an inactive one visible, flagged.</summary>
    public bool ProductIsActive { get; init; }

    public string MoldType { get; init; } = string.Empty;
    public int CavityCount { get; init; }
    public string? Manufacturer { get; init; }
    public string? SerialNumber { get; init; }
    public string? Location { get; init; }
    public string? StorageLocation { get; init; }
    public DateOnly? CommissionDate { get; init; }
    public int MaximumShots { get; init; }
    public int WarningShots { get; init; }
    public int ReplacementShots { get; init; }
    /// <summary>The usage-based PM interval; null = usage-based PM disabled.</summary>
    public int? MaintenanceFrequencyShots { get; init; }

    /// <summary>Cumulative shots - the one authoritative counter.</summary>
    public int CurrentUsageShots { get; init; }

    /// <summary>PM warning margin in remaining shots; null = no warning.</summary>
    public int? PmWarningShots { get; init; }

    /// <summary>Start of the current PM cycle (system-managed).</summary>
    public int PmCycleStartShots { get; init; }

    /// <summary>Cycle start + interval; null when PM is disabled.</summary>
    public long? PmNextThresholdShots { get; init; }

    /// <summary>Next threshold - current shots (0 or negative = due); null when PM is disabled.</summary>
    public long? PmRemainingShots { get; init; }
    public int? ResponsibleEmployeeId { get; init; }
    public string? ResponsibleEmployeeName { get; init; }

    /// <summary>Null when there is no responsible person.</summary>
    public bool? ResponsibleEmployeeIsActive { get; init; }

    /// <summary>Available / In Production / Maintenance / Replacement Due / Retired.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Normal / Warning / Replace - the persisted computed column life_state.</summary>
    public string LifeState { get; init; } = string.Empty;

    /// <summary>MIN(100, ROUND(CurrentUsage / MaxShots * 100)) - analysis 8.2.</summary>
    public int LifeUsedPercent { get; init; }

    /// <summary>MAX(0, MaxShots - CurrentUsage) - analysis 8.2.</summary>
    public int RemainingShots { get; init; }

    public string? Remarks { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the mold changed in between, the update is
    /// refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
