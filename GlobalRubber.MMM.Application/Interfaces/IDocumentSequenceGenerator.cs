namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Issues business codes (USR-0001, ...) from configuration.document_sequence_configuration - the
/// documented mechanism (BR-01): last_number is incremented under UPDLOCK, HOLDLOCK, never
/// MAX(code)+1. Call it inside the same database transaction as the insert that uses the code, so a
/// failed insert does not burn a number.
/// </summary>
public interface IDocumentSequenceGenerator
{
    /// <summary>Next code for the given document type, formatted PREFIX-NNNN using the row's own prefix/pad_length.</summary>
    /// <exception cref="InvalidOperationException">No active sequence is configured for the document type.</exception>
    Task<string> NextCodeAsync(string documentType, CancellationToken cancellationToken);
}
