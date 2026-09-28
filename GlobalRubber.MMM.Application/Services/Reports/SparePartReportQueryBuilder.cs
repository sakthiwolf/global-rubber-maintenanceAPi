using System.Linq.Expressions;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services.Reports;

/// <summary>Which master-based Spare Part report a stock query is for.</summary>
public enum SparePartStockScope
{
    /// <summary>Every spare part.</summary>
    Stock,

    /// <summary>Parts whose stock_status is Low Stock or Out of Stock (the existing BR-29 column, no new threshold).</summary>
    LowStock,

    /// <summary>Every spare part, valued at current stock x current unit cost.</summary>
    Valuation,
}

/// <summary>
/// The Spare Part Reports' filters, sorts, projections and aggregates as IQueryable compositions - translated to SQL by
/// EF Core (SparePartReportRepository) and run in memory by the tests. Sources, never mixed:
///   stock / low stock / valuation - masters.spare_part_master (current_stock, persisted stock_status, CURRENT unit_cost)
///   stock movement                - transactions.spare_part_stock_transaction (the ledger, signed quantity)
///   usage                         - transactions.spare_part_usage_transaction (cost = quantity x unit_cost_at_issue)
/// Stored UTC times are shown in plant (IST) time. Expects queries validated/canonicalised by SparePartReportService.
/// </summary>
public static class SparePartReportQueryBuilder
{
    private static readonly double PlantOffsetMinutes = PlantTime.UtcOffset.TotalMinutes;

    // ================================================================ Stock / Low Stock / Valuation (master)

    public static IQueryable<SparePart> FilterParts(IQueryable<SparePart> source, SparePartStockReportQuery q, SparePartStockScope scope)
    {
        if (scope == SparePartStockScope.LowStock)
            source = source.Where(p => p.StockStatus != SparePartStockStatus.Available);

        if (q.IsActive is { } isActive)
            source = source.Where(p => p.IsActive == isActive);

        if (!string.IsNullOrEmpty(q.StockStatus))
        {
            var status = q.StockStatus;
            source = source.Where(p => p.StockStatus == status);
        }

        if (q.VendorId is { } vendorId)
            source = source.Where(p => p.VendorId == vendorId);

        if (q.MachineId is { } machineId)
            source = source.Where(p => p.MachineId == machineId);

        if (!string.IsNullOrEmpty(q.Category))
        {
            var category = q.Category;
            source = source.Where(p => p.Category == category);
        }

        var search = Lowered(q.Search);
        if (search is not null)
        {
            source = source.Where(p =>
                p.SparePartCode.ToLower().Contains(search) ||
                p.SparePartName.ToLower().Contains(search) ||
                (p.Category != null && p.Category.ToLower().Contains(search)) ||
                (p.PartNumber != null && p.PartNumber.ToLower().Contains(search)) ||
                (p.StoreLocation != null && p.StoreLocation.ToLower().Contains(search)));
        }

        return source;
    }

    /// <summary>
    /// Default: stock = code; low stock = stock status (Out of Stock first) then current stock; valuation = stock value,
    /// largest first. stockStatus sorts by severity (Out of Stock, Low Stock, Available), not alphabetically.
    /// </summary>
    public static IOrderedQueryable<SparePart> OrderParts(IQueryable<SparePart> source, SparePartStockReportQuery q, SparePartStockScope scope)
    {
        var sortBy = q.SortBy ?? scope switch
        {
            SparePartStockScope.LowStock => SparePartReportSort.StockStatus,
            SparePartStockScope.Valuation => SparePartReportSort.StockValue,
            _ => SparePartReportSort.Code,
        };
        var descending = q.SortDirection is null
            ? sortBy is not (SparePartReportSort.Code or SparePartReportSort.Name or SparePartReportSort.StockStatus)
            : q.SortDirection == ReportSortDirection.Descending;

        var ordered = sortBy switch
        {
            SparePartReportSort.Name => By(source, p => p.SparePartName, descending),
            SparePartReportSort.CurrentStock => By(source, p => p.CurrentStock, descending),
            SparePartReportSort.MinimumStock => By(source, p => p.MinimumStock, descending),
            SparePartReportSort.UnitCost => By(source, p => p.UnitCost, descending),
            SparePartReportSort.StockValue => By(source, p => p.UnitCost != null ? (decimal?)(p.CurrentStock * p.UnitCost.Value) : null, descending),
            SparePartReportSort.StockStatus => By(source,
                p => p.StockStatus == SparePartStockStatus.OutOfStock ? 1 : p.StockStatus == SparePartStockStatus.LowStock ? 2 : 3, descending),
            _ => By(source, p => p.SparePartCode, descending),
        };

        return sortBy == SparePartReportSort.StockStatus
            ? ordered.ThenBy(p => p.CurrentStock).ThenBy(p => p.SparePartCode)
            : ordered.ThenBy(p => p.SparePartCode).ThenBy(p => p.SparePartId);
    }

    /// <summary>The part with its machine/vendor names, current value and latest ledger balance (LedgerCheck set by the service).</summary>
    public static Expression<Func<SparePart, SparePartStockItemDto>> StockRow(IQueryable<SparePartStockTransaction> ledger) => p => new SparePartStockItemDto
    {
        SparePartId = p.SparePartId,
        SparePartCode = p.SparePartCode,
        SparePartName = p.SparePartName,
        Category = p.Category,
        PartNumber = p.PartNumber,
        MachineCode = p.Machine != null ? p.Machine.MachineCode : null,
        MachineName = p.Machine != null ? p.Machine.MachineName : null,
        VendorName = p.Vendor != null ? p.Vendor.VendorName : null,
        Unit = p.Unit,
        StoreLocation = p.StoreLocation,
        CurrentStock = p.CurrentStock,
        MinimumStock = p.MinimumStock,
        UnitCost = p.UnitCost,
        StockValue = p.UnitCost != null ? (decimal?)(p.CurrentStock * p.UnitCost.Value) : null,
        StockStatus = p.StockStatus,
        IsActive = p.IsActive,
        LedgerStock = ledger.Where(t => t.SparePartId == p.SparePartId)
            .OrderByDescending(t => t.StockTransactionId)
            .Select(t => (int?)t.NewStock)
            .FirstOrDefault(),
    };

    /// <summary>Matches / Mismatch / No Ledger: the master stock against the latest ledger row - reported, never corrected.</summary>
    public static string LedgerCheckOf(int currentStock, int? ledgerStock) =>
        ledgerStock is null ? SparePartLedgerCheck.NoLedger
        : ledgerStock == currentStock ? SparePartLedgerCheck.Matches
        : SparePartLedgerCheck.Mismatch;

    public static IQueryable<SparePartLowStockSummaryDto> LowStockSummary(IQueryable<SparePart> filtered) =>
        filtered.GroupBy(p => 1).Select(g => new SparePartLowStockSummaryDto
        {
            TotalAttention = g.Count(),
            LowStockCount = g.Count(p => p.StockStatus == SparePartStockStatus.LowStock),
            OutOfStockCount = g.Count(p => p.StockStatus == SparePartStockStatus.OutOfStock),
        });

    public static IQueryable<SparePartValuationSummaryDto> ValuationSummary(IQueryable<SparePart> filtered) =>
        filtered.GroupBy(p => 1).Select(g => new SparePartValuationSummaryDto
        {
            TotalParts = g.Count(),
            PartsWithoutUnitCost = g.Count(p => p.UnitCost == null),
            TotalValue = g.Sum(p => p.UnitCost != null ? p.CurrentStock * p.UnitCost.Value : 0m),
            LowStockValue = g.Sum(p => p.UnitCost != null && p.StockStatus != SparePartStockStatus.Available ? p.CurrentStock * p.UnitCost.Value : 0m),
        });

    // ================================================================ Stock Movement (ledger)

    /// <summary>Ledger filters on the ledger row itself (part, type, direction, IST date range).</summary>
    public static IQueryable<SparePartStockTransaction> FilterLedger(IQueryable<SparePartStockTransaction> source, SparePartMovementReportQuery q)
    {
        if (q.SparePartId is { } partId)
            source = source.Where(t => t.SparePartId == partId);

        if (!string.IsNullOrEmpty(q.TransactionType))
        {
            var type = q.TransactionType;
            source = source.Where(t => t.TransactionType == type);
        }

        source = q.Direction switch
        {
            SparePartMovementDirection.In => source.Where(t => t.Quantity > 0),
            SparePartMovementDirection.Out => source.Where(t => t.Quantity < 0),
            _ => source,
        };

        // transaction_at is UTC; the range is plant (IST) dates -> [from 00:00 IST, to+1 00:00 IST) in UTC.
        if (q.FromDate is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue) - PlantTime.UtcOffset;
            source = source.Where(t => t.TransactionAt >= fromUtc);
        }

        if (q.ToDate is { } to)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue) - PlantTime.UtcOffset;
            source = source.Where(t => t.TransactionAt < toUtc);
        }

        return source;
    }

    /// <summary>The ledger rows joined to their part and the posting user, then searched (part code/name, reference, remarks).</summary>
    public static IQueryable<SparePartMovementRow> MovementRows(
        IQueryable<SparePartStockTransaction> ledger, IQueryable<SparePart> parts, IQueryable<User> users, SparePartMovementReportQuery q)
    {
        var rows =
            from t in ledger
            join p in parts on t.SparePartId equals p.SparePartId
            select new SparePartMovementRow
            {
                StockTransactionId = t.StockTransactionId,
                TransactionAtUtc = t.TransactionAt,
                SparePartCode = p.SparePartCode,
                SparePartName = p.SparePartName,
                Unit = p.Unit,
                TransactionType = t.TransactionType,
                Quantity = t.Quantity,
                PreviousStock = t.PreviousStock,
                NewStock = t.NewStock,
                ReferenceType = t.ReferenceType,
                ReferenceNo = t.ReferenceNo,
                CreatedByName = users.Where(u => u.UserId == t.CreatedBy).Select(u => u.UserName).FirstOrDefault(),
                Remarks = t.Remarks,
            };

        var search = Lowered(q.Search);
        if (search is not null)
        {
            rows = rows.Where(r =>
                r.SparePartCode.ToLower().Contains(search) ||
                r.SparePartName.ToLower().Contains(search) ||
                (r.ReferenceNo != null && r.ReferenceNo.ToLower().Contains(search)) ||
                (r.Remarks != null && r.Remarks.ToLower().Contains(search)));
        }

        return rows;
    }

    /// <summary>date (default, newest first) / sparePart / quantity / type / newStock; ties newest record first.</summary>
    public static IOrderedQueryable<SparePartMovementRow> OrderMovements(IQueryable<SparePartMovementRow> rows, SparePartMovementReportQuery q)
    {
        var sortBy = q.SortBy ?? SparePartReportSort.Date;
        var descending = q.SortDirection is null
            ? sortBy is not (SparePartReportSort.SparePart or SparePartReportSort.Type)
            : q.SortDirection == ReportSortDirection.Descending;

        var ordered = sortBy switch
        {
            SparePartReportSort.SparePart => By(rows, r => r.SparePartCode, descending),
            SparePartReportSort.Quantity => By(rows, r => r.Quantity, descending),
            SparePartReportSort.Type => By(rows, r => r.TransactionType, descending),
            SparePartReportSort.NewStock => By(rows, r => r.NewStock, descending),
            _ => By(rows, r => r.TransactionAtUtc, descending),
        };

        return ordered.ThenByDescending(r => r.StockTransactionId);
    }

    public static readonly Expression<Func<SparePartMovementRow, SparePartMovementItemDto>> MovementItem = r => new SparePartMovementItemDto
    {
        StockTransactionId = r.StockTransactionId,
        TransactionAt = r.TransactionAtUtc.AddMinutes(PlantOffsetMinutes),
        SparePartCode = r.SparePartCode,
        SparePartName = r.SparePartName,
        Unit = r.Unit,
        TransactionType = r.TransactionType,
        Direction = r.Quantity > 0 ? SparePartMovementDirection.In : r.Quantity < 0 ? SparePartMovementDirection.Out : null,
        Quantity = r.Quantity,
        PreviousStock = r.PreviousStock,
        NewStock = r.NewStock,
        ReferenceType = r.ReferenceType,
        ReferenceNo = r.ReferenceNo,
        CreatedByName = r.CreatedByName,
        Remarks = r.Remarks,
    };

    // ================================================================ Spare Part Usage

    public static IQueryable<SparePartUsage> FilterUsage(IQueryable<SparePartUsage> source, SparePartUsageReportQuery q)
    {
        if (q.SparePartId is { } partId)
            source = source.Where(u => u.SparePartId == partId);

        if (q.MachineId is { } machineId)
            source = source.Where(u => u.MachineId == machineId);

        if (q.MoldId is { } moldId)
            source = source.Where(u => u.MoldId == moldId);

        if (!string.IsNullOrEmpty(q.UsedFor))
        {
            var usedFor = q.UsedFor;
            source = source.Where(u => u.UsedFor == usedFor);
        }

        if (!string.IsNullOrEmpty(q.Status))
        {
            var status = q.Status;
            source = source.Where(u => u.Status == status);
        }

        if (q.FromDate is { } from)
            source = source.Where(u => u.UsageDate >= from);

        if (q.ToDate is { } to)
            source = source.Where(u => u.UsageDate <= to);

        var search = Lowered(q.Search);
        if (search is not null)
        {
            source = source.Where(u =>
                u.UsageNo.ToLower().Contains(search) ||
                u.SparePart.SparePartCode.ToLower().Contains(search) ||
                u.SparePart.SparePartName.ToLower().Contains(search) ||
                (u.Machine != null && u.Machine.MachineCode.ToLower().Contains(search)) ||
                (u.Mold != null && u.Mold.MoldCode.ToLower().Contains(search)) ||
                (u.MachinePm != null && u.MachinePm.PmNo.ToLower().Contains(search)) ||
                (u.MoldPm != null && u.MoldPm.PmNo.ToLower().Contains(search)) ||
                (u.UsedByEmployee != null && u.UsedByEmployee.EmployeeName.ToLower().Contains(search)) ||
                (u.Remarks != null && u.Remarks.ToLower().Contains(search)));
        }

        return source;
    }

    /// <summary>date (default, newest first) / sparePart / quantity / totalCost (quantity x cost at issue) / status.</summary>
    public static IOrderedQueryable<SparePartUsage> OrderUsage(IQueryable<SparePartUsage> source, SparePartUsageReportQuery q)
    {
        var sortBy = q.SortBy ?? SparePartReportSort.Date;
        var descending = q.SortDirection is null
            ? sortBy is not (SparePartReportSort.SparePart or SparePartReportSort.Status)
            : q.SortDirection == ReportSortDirection.Descending;

        var ordered = sortBy switch
        {
            SparePartReportSort.SparePart => By(source, u => u.SparePart.SparePartCode, descending),
            SparePartReportSort.Quantity => By(source, u => u.Quantity, descending),
            SparePartReportSort.TotalCost => By(source, u => u.UnitCostAtIssue != null ? (decimal?)(u.Quantity * u.UnitCostAtIssue.Value) : null, descending),
            SparePartReportSort.Status => By(source, u => u.Status, descending),
            _ => By(source, u => u.UsageDate, descending),
        };

        return ordered.ThenByDescending(u => u.SparePartUsageId);
    }

    public static Expression<Func<SparePartUsage, SparePartUsageItemDto>> UsageRow(IQueryable<User> users) => u => new SparePartUsageItemDto
    {
        SparePartUsageId = u.SparePartUsageId,
        UsageNo = u.UsageNo,
        UsageDate = u.UsageDate,
        SparePartCode = u.SparePart.SparePartCode,
        SparePartName = u.SparePart.SparePartName,
        Unit = u.SparePart.Unit,
        Quantity = u.Quantity,
        UnitCostAtIssue = u.UnitCostAtIssue,
        TotalCost = u.UnitCostAtIssue != null ? (decimal?)(u.Quantity * u.UnitCostAtIssue.Value) : null,
        UsedFor = u.UsedFor,
        MachineCode = u.Machine != null ? u.Machine.MachineCode : null,
        MachineName = u.Machine != null ? u.Machine.MachineName : null,
        MoldCode = u.Mold != null ? u.Mold.MoldCode : null,
        MoldName = u.Mold != null ? u.Mold.MoldName : null,
        MachinePmNo = u.MachinePm != null ? u.MachinePm.PmNo : null,
        MoldPmNo = u.MoldPm != null ? u.MoldPm.PmNo : null,
        UsedByName = u.UsedByEmployee != null ? u.UsedByEmployee.EmployeeName : null,
        Status = u.Status,
        CreatedAt = u.CreatedAt.AddMinutes(PlantOffsetMinutes),
        CreatedByName = users.Where(x => x.UserId == u.CreatedBy).Select(x => x.UserName).FirstOrDefault(),
        ReversedAt = u.ReversedAt != null ? u.ReversedAt.Value.AddMinutes(PlantOffsetMinutes) : null,
        ReversalReason = u.ReversalReason,
        Remarks = u.Remarks,
    };

    // ================================================================ helpers

    private static string? Lowered(string? search)
    {
        var trimmed = search?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLower();
    }

    private static IOrderedQueryable<T> By<T, TKey>(IQueryable<T> source, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? source.OrderByDescending(key) : source.OrderBy(key);
}

/// <summary>A ledger row joined to its part (UTC time kept for filtering/sorting; converted to IST in the DTO).</summary>
public sealed class SparePartMovementRow
{
    public long StockTransactionId { get; init; }
    public DateTime TransactionAtUtc { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string TransactionType { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int PreviousStock { get; init; }
    public int NewStock { get; init; }
    public string? ReferenceType { get; init; }
    public string? ReferenceNo { get; init; }
    public string? CreatedByName { get; init; }
    public string? Remarks { get; init; }
}
