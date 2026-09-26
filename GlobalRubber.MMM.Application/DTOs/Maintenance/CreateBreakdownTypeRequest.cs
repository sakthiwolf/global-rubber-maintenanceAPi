namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Request body for POST /api/v1/breakdown-types: create a new breakdown type.</summary>
public sealed class CreateBreakdownTypeRequest
{
    /// <summary>Name of the breakdown type; required, max 100 characters. Duplicate active names are rejected.</summary>
    public string BreakdownTypeName { get; set; } = string.Empty;
}