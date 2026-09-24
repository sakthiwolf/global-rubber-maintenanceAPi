namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// document_type values exactly as seeded in database/scripts/010_seed_data.sql
/// (configuration.document_sequence_configuration) - transcribed, not invented. Only the types the
/// backend actually issues codes for so far are listed; add the others as their modules arrive.
/// </summary>
public static class DocumentTypes
{
    public const string User = "USER";
}
