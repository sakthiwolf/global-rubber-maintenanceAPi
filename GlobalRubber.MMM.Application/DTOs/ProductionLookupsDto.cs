namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Dropdown data for the Production Entry form (GET /api/v1/production-entries/lookups, analysis 18.2 / BR-13): active
/// machines, active products, and the molds with the life figures the Mold Life panel needs. Served under the Production
/// Entry permission so a Production User - who has no Product master permission in the seed - can use the form.
/// </summary>
public sealed class ProductionLookupsDto
{
    public IReadOnlyList<ProductionMachineLookupDto> Machines { get; init; } = Array.Empty<ProductionMachineLookupDto>();
    public IReadOnlyList<ProductionProductLookupDto> Products { get; init; } = Array.Empty<ProductionProductLookupDto>();
    public IReadOnlyList<ProductionMoldLookupDto> Molds { get; init; } = Array.Empty<ProductionMoldLookupDto>();
}

/// <summary>An active machine.</summary>
public sealed class ProductionMachineLookupDto
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
}

/// <summary>An active product.</summary>
public sealed class ProductionProductLookupDto
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
}

/// <summary>A mold of an active product, in any status (analysis 6.1 / Q-09), with its life figures.</summary>
public sealed class ProductionMoldLookupDto
{
    public int MoldId { get; init; }
    public string MoldCode { get; init; } = string.Empty;
    public string MoldName { get; init; } = string.Empty;
    public int ProductId { get; init; }
    public int CurrentUsageShots { get; init; }
    public int MaximumShots { get; init; }
    public int WarningShots { get; init; }
    public int ReplacementShots { get; init; }

    /// <summary>Normal / Warning / Replace (computed by SQL Server).</summary>
    public string LifeState { get; init; } = string.Empty;

    /// <summary>The mold's status (Available / In Production / Maintenance / Replacement Due / Retired).</summary>
    public string Status { get; init; } = string.Empty;
}
