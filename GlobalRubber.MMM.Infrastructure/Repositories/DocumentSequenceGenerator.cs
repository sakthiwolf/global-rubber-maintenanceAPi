using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// The documented convention (database/scripts/004_create_configuration_tables.sql), the same
/// statement the bootstrap-admin command already uses to issue USR-0001: last_number is incremented
/// under UPDLOCK, HOLDLOCK and the new value read back in one atomic statement. Raw parameterized SQL
/// rather than a new entity/DbSet, since only this generator touches the table. It runs on the shared
/// DbContext, so inside a caller's transaction the increment commits or rolls back with the insert.
/// </summary>
public sealed class DocumentSequenceGenerator : IDocumentSequenceGenerator
{
    private readonly GlobalRubberDbContext _dbContext;

    public DocumentSequenceGenerator(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextCodeAsync(string documentType, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Database.SqlQueryRaw<SequenceRow>(
            "UPDATE configuration.document_sequence_configuration WITH (UPDLOCK, HOLDLOCK) " +
            "SET last_number = last_number + 1 " +
            "OUTPUT INSERTED.last_number AS LastNumber, INSERTED.prefix AS Prefix, INSERTED.pad_length AS PadLength " +
            "WHERE document_type = {0} AND is_active = 1;",
            documentType).ToListAsync(cancellationToken);

        var row = rows.SingleOrDefault()
            ?? throw new InvalidOperationException($"No active document sequence is configured for '{documentType}'.");

        return Format(row.Prefix, row.LastNumber, row.PadLength);
    }

    internal static string Format(string prefix, int number, int padLength) =>
        $"{prefix}-{number.ToString(new string('0', padLength))}";

    private sealed class SequenceRow
    {
        public int LastNumber { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public byte PadLength { get; set; }
    }
}
