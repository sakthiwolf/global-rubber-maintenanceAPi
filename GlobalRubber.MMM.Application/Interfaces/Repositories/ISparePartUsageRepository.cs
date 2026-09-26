using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface ISparePartUsageRepository
{
    /// <summary>A page of usages (spare part, machine, mold, PM and employee included), newest first.</summary>
    Task<(IReadOnlyList<SparePartUsage> Items, int TotalCount)> GetAllAsync(SparePartUsageListQuery query, CancellationToken cancellationToken);

    /// <summary>One usage with its navigations, untracked; null if it does not exist.</summary>
    Task<SparePartUsage?> GetByIdAsync(int sparePartUsageId, CancellationToken cancellationToken);

    /// <summary>The ledger rows a usage caused, oldest first.</summary>
    Task<IReadOnlyList<SparePartStockTransaction>> GetStockMovementsAsync(int sparePartUsageId, CancellationToken cancellationToken);

    /// <summary>The Add form's lookups; "due" Machine PMs compare their date with <paramref name="today"/> (IST).</summary>
    Task<SparePartUsageLookupsDto> GetLookupsAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>The usage already posted with this idempotency key, if any.</summary>
    Task<SparePartUsage?> GetByRequestIdAsync(Guid requestId, CancellationToken cancellationToken);

    /// <summary>
    /// ONE transaction: lock the spare part row (UPDLOCK - concurrent issues of the same part queue here), read and lock the
    /// maintenance record (<paramref name="maintenanceType"/> + id; null when it does not exist), then
    /// <paramref name="applyToLocked"/> (the service's rules: may throw - nothing is written; sets the usage fields, the new
    /// stock, and returns the ledger row to write), issue the usage number (SPU), insert the usage, update the stock, insert
    /// the ledger row, then <paramref name="afterSave"/> (stock notifications) and commit. A duplicate request id raises
    /// <see cref="DuplicateRequestException"/> (the caller returns the existing usage).
    /// </summary>
    Task<SparePartUsage> AddAsync(
        SparePartUsage usage, string maintenanceType, int maintenanceId,
        Func<SparePart, MaintenanceReference?, SparePartStockTransaction> applyToLocked,
        Func<SparePart, SparePartUsage, CancellationToken, Task>? afterSave,
        CancellationToken cancellationToken);

    /// <summary>
    /// ONE transaction: lock the spare part row, load the usage, <paramref name="applyToLocked"/> (status, stock restore;
    /// returns the Reversal ledger row), write the usage guarded by <paramref name="originalRowVersion"/> (stale -&gt; 409),
    /// the stock and the ledger row, commit.
    /// </summary>
    Task<SparePartUsage> ReverseAsync(
        int sparePartUsageId, byte[] originalRowVersion,
        Func<SparePartUsage, SparePart, SparePartStockTransaction> applyToLocked, CancellationToken cancellationToken);
}

/// <summary>The idempotency key was already used (UX_spare_part_usage_transaction_request_id).</summary>
public sealed class DuplicateRequestException : Exception
{
    public DuplicateRequestException(Guid requestId)
        : base($"A spare part usage was already posted for request {requestId}.")
    {
        RequestId = requestId;
    }

    public Guid RequestId { get; }
}
