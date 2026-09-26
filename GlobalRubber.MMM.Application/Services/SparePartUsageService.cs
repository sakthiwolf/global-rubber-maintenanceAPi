using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Spare Part Usage - parts issued against real maintenance (decisions approved 2026-09-25, migration 016).
///
/// Posting (one transaction, spare part row locked): the maintenance must be a Machine PM that is not completed and is due
/// (scheduled on or before today's IST date) or an open Mold PM (Scheduled / In Progress) - verified on the server by id;
/// the spare part must exist and be active; the quantity is a whole number &gt; 0 (stock is INT) and no more than the
/// available stock ("Insufficient stock available." - 409, stock never negative). The machine / mold come from the PM, the
/// unit from the master, the unit cost is snapshotted. The usage, the stock deduction and an Issue ledger row are written
/// together; a transition INTO Low Stock / Out of Stock writes one notification (recipients: View on Spare Part) inside
/// the same transaction behind a savepoint (a notification failure never fails the usage). A retried request with the
/// same request id returns the usage already posted. Used By is an optional active employee; the posting user is
/// created_by. Audit after commit: SparePartUsageCreated (+ SparePartLowStock / SparePartOutOfStock as the system).
///
/// Reversal (DELETE, Delete permission - never a physical delete): Posted -&gt; Reversed with a required reason, the stock
/// is restored and a Reversal ledger row written, guarded by the usage's row version. Posted usage is never edited.
/// </summary>
public sealed class SparePartUsageService : ISparePartUsageService
{
    private const string Module = "Spare Part Usage";
    private const string EntityName = "SparePartUsage";
    private const int RemarksMaxLength = 500;
    private const int ReasonMaxLength = 500;
    public const string InsufficientStockMessage = "Insufficient stock available.";
    public const string SparePartsLinkPath = "/masters/spare-parts";

    private readonly ISparePartUsageRepository _repository;
    private readonly ISparePartRepository _sparePartRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SparePartUsageService> _logger;

    public SparePartUsageService(
        ISparePartUsageRepository repository,
        ISparePartRepository sparePartRepository,
        IEmployeeRepository employeeRepository,
        INotificationRepository notificationRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<SparePartUsageService> logger)
    {
        _repository = repository;
        _sparePartRepository = sparePartRepository;
        _employeeRepository = employeeRepository;
        _notificationRepository = notificationRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Raw(long value) => value.ToString(CultureInfo.InvariantCulture);

    public async Task<PagedResult<SparePartUsageDto>> GetAllAsync(SparePartUsageListQuery query, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.MaintenanceType) && !SparePartUsageMaintenanceType.All.Contains(query.MaintenanceType))
            errors.Add($"MaintenanceType must be one of: {string.Join(", ", SparePartUsageMaintenanceType.All)}.");
        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status is not (SparePartUsageStatus.Posted or SparePartUsageStatus.Reversed))
            errors.Add($"Status must be one of: {SparePartUsageStatus.Posted}, {SparePartUsageStatus.Reversed}.");
        if (query.DateFrom is { } from && query.DateTo is { } to && from > to)
            errors.Add("DateFrom cannot be after DateTo.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var (items, total) = await _repository.GetAllAsync(query, cancellationToken);
        return PagedResult<SparePartUsageDto>.Create(items.Select(u => MapToDto(u)).ToList(), query.PageNumber, query.PageSize, total);
    }

    public async Task<SparePartUsageDto> GetByIdAsync(int sparePartUsageId, CancellationToken cancellationToken)
    {
        var usage = await _repository.GetByIdAsync(sparePartUsageId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePartUsage), sparePartUsageId);
        var movements = await _repository.GetStockMovementsAsync(sparePartUsageId, cancellationToken);
        return MapToDto(usage, movements);
    }

    public Task<SparePartUsageLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(_dateTimeProvider.Today, cancellationToken);

    public async Task<(SparePartUsageDto Usage, bool Created)> CreateAsync(
        CreateSparePartUsageRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var input = NormalizeAndValidate(request);

        // Idempotency: the same request id again returns what was posted (only if it really is the same usage).
        if (input.RequestId is { } requestId && await _repository.GetByRequestIdAsync(requestId, cancellationToken) is { } already)
        {
            return (ReturnExisting(already, input), false);
        }

        // Clean 404s up front; the rules that decide (active, open, due, stock) run on the LOCKED rows inside the save.
        _ = await _sparePartRepository.GetByIdAsync(input.SparePartId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePart), input.SparePartId);
        if (input.UsedByEmployeeId is { } employeeId)
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
                ?? throw new NotFoundException(nameof(Employee), employeeId);
            if (!employee.IsActive) throw new ValidationException("The selected employee (Used By) is not active.");
        }

        var today = _dateTimeProvider.Today;
        var now = _dateTimeProvider.UtcNow;
        var usage = new SparePartUsage
        {
            // Id / number / asset / unit cost are never client-controlled.
            SparePartId = input.SparePartId,
            Quantity = input.Quantity,
            UsageDate = input.UsageDate,
            UsedFor = SparePartUsageMaintenanceType.UsedForOf(input.MaintenanceType),
            UsedByEmployeeId = input.UsedByEmployeeId,
            Remarks = input.Remarks,
            Status = SparePartUsageStatus.Posted,
            RequestId = input.RequestId,
            CreatedAt = now,
            CreatedBy = actingUserId,
        };

        // Captured on the locked rows; used after the commit (audit) and in afterSave (notifications).
        var stock = (Before: 0, After: 0, Minimum: 0, Code: string.Empty, Name: string.Empty, Unit: string.Empty, Pm: string.Empty, Asset: string.Empty);
        string? transition = null;
        var notificationWritten = false;

        SparePartUsage created;
        try
        {
            created = await _repository.AddAsync(usage, input.MaintenanceType, input.MaintenanceId, (part, pm) =>
            {
                if (pm is null)
                {
                    throw new NotFoundException($"The selected {input.MaintenanceType} ({input.MaintenanceId}) was not found.");
                }

                if (pm.Status == MachinePmStatus.Completed)
                {
                    throw new ValidationException($"Spare parts can only be issued against open maintenance: {pm.MaintenanceNo} is already completed.");
                }

                // Only DUE Machine PMs (the recurring successor is created in advance and stays hidden until its date).
                if (input.MaintenanceType == SparePartUsageMaintenanceType.MachinePm && pm.ScheduledDate > today)
                {
                    throw new ValidationException($"{pm.MaintenanceNo} is not due yet (scheduled {pm.ScheduledDate:yyyy-MM-dd}).");
                }

                if (!part.IsActive)
                {
                    throw new ValidationException("The selected spare part is not active.");
                }

                if (!SparePartStockRules.CanIssue(part.CurrentStock, input.Quantity))
                {
                    throw new ConflictException(
                        $"{InsufficientStockMessage} Available: {N(part.CurrentStock)} {part.Unit}, requested: {N(input.Quantity)} {part.Unit}.");
                }

                stock = (part.CurrentStock, part.CurrentStock - input.Quantity, part.MinimumStock, part.SparePartCode, part.SparePartName, part.Unit, pm.MaintenanceNo, pm.AssetCode);

                usage.MachinePmId = input.MaintenanceType == SparePartUsageMaintenanceType.MachinePm ? pm.MaintenanceId : null;
                usage.MoldPmId = input.MaintenanceType == SparePartUsageMaintenanceType.MoldPm ? pm.MaintenanceId : null;
                usage.MachineId = pm.MachineId;
                usage.MoldId = pm.MoldId;
                usage.ReferenceNo = pm.MaintenanceNo; // the PM number, kept with the usage
                usage.UnitCostAtIssue = part.UnitCost;  // historical cost never follows later price changes

                part.CurrentStock = stock.After;
                part.UpdatedAt = now;
                part.UpdatedBy = actingUserId;

                return new SparePartStockTransaction
                {
                    SparePartId = part.SparePartId,
                    TransactionType = SparePartStockTransactionType.Issue,
                    Quantity = -input.Quantity,
                    PreviousStock = stock.Before,
                    NewStock = stock.After,
                    ReferenceType = SparePartStockReferenceType.SparePartUsage,
                    CreatedBy = actingUserId,
                    Remarks = $"Issued for {pm.MaintenanceNo} ({pm.AssetCode}).",
                };
            }, async (part, saved, ct) =>
            {
                transition = SparePartStockRules.AlertTransition(stock.Before, stock.After, stock.Minimum);
                if (transition is null) return;

                var outOfStock = transition == SparePartStockStatus.OutOfStock;
                var result = await _notificationRepository.TryAddAsync(new Notification
                {
                    NotificationType = outOfStock ? NotificationTypes.SparePartOutOfStock : NotificationTypes.SparePartLowStock,
                    ModuleCode = ModuleCodes.MasterSparePart,
                    Severity = outOfStock ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    Title = outOfStock ? $"Spare part {stock.Code} is out of stock" : $"Spare part {stock.Code} is low on stock",
                    Message = outOfStock
                        ? $"Spare part {stock.Code} - {stock.Name} is out of stock (0 {stock.Unit}) after {saved.UsageNo} for {stock.Pm}."
                        : $"Spare part {stock.Code} - {stock.Name} is low on stock: {N(stock.After)} {stock.Unit} left (minimum {N(stock.Minimum)}) after {saved.UsageNo} for {stock.Pm}.",
                    EntityName = "SparePart",
                    EntityId = part.SparePartId,
                    RecordRef = stock.Code,
                    LinkPath = SparePartsLinkPath,
                    // One notification per transition: keyed by the usage that caused it.
                    EventKey = string.Create(CultureInfo.InvariantCulture, $"SPARE_PART_{(outOfStock ? "OUT" : "LOW")}:{part.SparePartId}:{saved.SparePartUsageId}"),
                    CreatedBy = null,
                }, ct);
                notificationWritten = result == NotificationWriteResult.Added;
                if (result == NotificationWriteResult.Failed)
                {
                    _logger.LogWarning("The stock notification for spare part {Code} could not be written; the usage was saved.", stock.Code);
                }
            }, cancellationToken);
        }
        catch (DuplicateRequestException ex)
        {
            // A concurrent duplicate of the same request won the unique index: return the usage it posted.
            var existing = await _repository.GetByRequestIdAsync(ex.RequestId, cancellationToken)
                ?? throw new ConflictException("The request was already submitted. Refresh the list.");
            return (ReturnExisting(existing, input), false);
        }

        var actorName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);
        var statusBefore = SparePartStockRules.StatusOf(stock.Before, stock.Minimum);
        var statusAfter = SparePartStockRules.StatusOf(stock.After, stock.Minimum);
        var details = new List<AuditLogDetailEntry>
        {
            new("spare_part_code", null, stock.Code),
            new("quantity", null, Raw(created.Quantity)),
            new("unit", null, stock.Unit),
            new("maintenance_no", null, stock.Pm),
            new($"current_stock ({stock.Code})", Raw(stock.Before), Raw(stock.After)),
        };
        if (statusBefore != statusAfter) details.Add(new AuditLogDetailEntry($"stock_status ({stock.Code})", statusBefore, statusAfter));
        if (created.UnitCostAtIssue is { } cost) details.Add(new AuditLogDetailEntry("unit_cost_at_issue", null, cost.ToString("0.00", CultureInfo.InvariantCulture)));

        await LogAsync(actingUserId, actorName, "SparePartUsageCreated", created.SparePartUsageId, created.UsageNo,
            $"{actorName} issued {N(created.Quantity)} {stock.Unit} of {stock.Name} ({stock.Code}) for {stock.Pm} ({stock.Asset}) - {created.UsageNo}. " +
            $"Stock changed from {N(stock.Before)} to {N(stock.After)}.",
            details, ipAddress, cancellationToken);

        if (transition is not null)
        {
            var action = transition == SparePartStockStatus.OutOfStock ? "SparePartOutOfStock" : "SparePartLowStock";
            await LogAsync(null, MoldPmEvaluator.SystemUserName, action, created.SparePartUsageId, created.UsageNo,
                $"System: spare part {stock.Code} moved to {transition} ({N(stock.After)} {stock.Unit}, minimum {N(stock.Minimum)}) after {created.UsageNo} by {actorName}." +
                (notificationWritten ? " Notification sent." : " The notification could not be written."),
                new[] { new AuditLogDetailEntry($"stock_status ({stock.Code})", statusBefore, statusAfter) }, ipAddress, cancellationToken);
        }

        var dto = await _repository.GetByIdAsync(created.SparePartUsageId, cancellationToken) ?? created;
        return (MapToDto(dto, await _repository.GetStockMovementsAsync(created.SparePartUsageId, cancellationToken)), true);
    }

    public async Task<SparePartUsageDto> ReverseAsync(
        int sparePartUsageId, ReverseSparePartUsageRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var errors = new List<string>();
        if (reason is null) errors.Add("Reason is required.");
        else if (reason.Length > ReasonMaxLength) errors.Add($"Reason must be at most {ReasonMaxLength} characters.");
        byte[]? originalRowVersion = null;
        if (string.IsNullOrWhiteSpace(request.RowVersion)) errors.Add("RowVersion is required.");
        else if (!TryDecodeRowVersion(request.RowVersion, out originalRowVersion)) errors.Add("RowVersion is not valid.");
        if (errors.Count > 0) throw new ValidationException(errors);

        var existing = await _repository.GetByIdAsync(sparePartUsageId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePartUsage), sparePartUsageId);
        if (existing.Status == SparePartUsageStatus.Reversed)
        {
            throw new ConflictException("The spare part usage is already reversed.");
        }

        var now = _dateTimeProvider.UtcNow;
        var stock = (Before: 0, After: 0, Code: string.Empty, Unit: string.Empty);

        var reversed = await _repository.ReverseAsync(sparePartUsageId, originalRowVersion!, (usage, part) =>
        {
            if (usage.Status == SparePartUsageStatus.Reversed)
            {
                throw new ConflictException("The spare part usage is already reversed.");
            }

            stock = (part.CurrentStock, part.CurrentStock + usage.Quantity, part.SparePartCode, part.Unit);
            part.CurrentStock = stock.After;
            part.UpdatedAt = now;
            part.UpdatedBy = actingUserId;

            usage.Status = SparePartUsageStatus.Reversed;
            usage.ReversedAt = now;
            usage.ReversedBy = actingUserId;
            usage.ReversalReason = reason;
            usage.UpdatedAt = now;
            usage.UpdatedBy = actingUserId;

            return new SparePartStockTransaction
            {
                SparePartId = part.SparePartId,
                TransactionType = SparePartStockTransactionType.Reversal,
                Quantity = usage.Quantity,
                PreviousStock = stock.Before,
                NewStock = stock.After,
                ReferenceType = SparePartStockReferenceType.SparePartUsage,
                ReferenceId = usage.SparePartUsageId,
                ReferenceNo = usage.UsageNo,
                CreatedBy = actingUserId,
                Remarks = $"Reversal of {usage.UsageNo}: {reason}",
            };
        }, cancellationToken);

        var actorName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);
        await LogAsync(actingUserId, actorName, "SparePartUsageReversed", reversed.SparePartUsageId, reversed.UsageNo,
            $"{actorName} reversed {reversed.UsageNo} ({N(reversed.Quantity)} {stock.Unit} of {stock.Code}): {reason}. Stock restored from {N(stock.Before)} to {N(stock.After)}.",
            new[]
            {
                new AuditLogDetailEntry("status", SparePartUsageStatus.Posted, SparePartUsageStatus.Reversed),
                new AuditLogDetailEntry("reversal_reason", null, reason),
                new AuditLogDetailEntry($"current_stock ({stock.Code})", Raw(stock.Before), Raw(stock.After)),
            }, ipAddress, cancellationToken);

        var fresh = await _repository.GetByIdAsync(sparePartUsageId, cancellationToken) ?? reversed;
        return MapToDto(fresh, await _repository.GetStockMovementsAsync(sparePartUsageId, cancellationToken));
    }

    private sealed record ValidInput(
        string MaintenanceType, int MaintenanceId, int SparePartId, int Quantity, DateOnly UsageDate, int? UsedByEmployeeId, string? Remarks, Guid? RequestId);

    private static ValidInput NormalizeAndValidate(CreateSparePartUsageRequest request)
    {
        var typeInput = string.IsNullOrWhiteSpace(request.MaintenanceType) ? null : request.MaintenanceType.Trim();
        var type = SparePartUsageMaintenanceType.All.FirstOrDefault(t => string.Equals(t, typeInput, StringComparison.OrdinalIgnoreCase));
        var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

        var errors = new List<string>();
        if (typeInput is null) errors.Add("MaintenanceType is required.");
        else if (type is null) errors.Add($"MaintenanceType must be one of: {string.Join(", ", SparePartUsageMaintenanceType.All)}.");
        if (request.MaintenanceId is null or <= 0) errors.Add("MaintenanceId is required.");
        if (request.SparePartId is null or <= 0) errors.Add("SparePartId is required.");

        // Stock is a whole number (INT), so the usage is too: 1.5 is refused rather than rounded.
        if (request.Quantity is null) errors.Add("Quantity is required.");
        else if (request.Quantity <= 0) errors.Add("Quantity must be greater than 0.");
        else if (request.Quantity != decimal.Truncate(request.Quantity.Value)) errors.Add("Quantity must be a whole number.");
        else if (request.Quantity > int.MaxValue) errors.Add("Quantity is too large.");

        if (request.UsageDate is null) errors.Add("UsageDate is required.");
        if (request.UsedByEmployeeId is <= 0) errors.Add("UsedByEmployeeId is not valid.");
        if (remarks is { Length: > RemarksMaxLength }) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");
        if (request.RequestId == Guid.Empty) errors.Add("RequestId is not valid.");
        if (errors.Count > 0) throw new ValidationException(errors);

        return new ValidInput(type!, request.MaintenanceId!.Value, request.SparePartId!.Value, (int)request.Quantity!.Value,
            request.UsageDate!.Value, request.UsedByEmployeeId, remarks, request.RequestId);
    }

    // A replayed request must be the SAME usage; reusing a key for different data is a client bug worth a 409.
    private SparePartUsageDto ReturnExisting(SparePartUsage existing, ValidInput input)
    {
        var sameMaintenance = input.MaintenanceType == SparePartUsageMaintenanceType.MachinePm
            ? existing.MachinePmId == input.MaintenanceId
            : existing.MoldPmId == input.MaintenanceId;
        if (existing.SparePartId != input.SparePartId || existing.Quantity != input.Quantity || !sameMaintenance)
        {
            throw new ConflictException("This request id was already used for a different spare part usage.");
        }

        return MapToDto(existing);
    }

    private static bool TryDecodeRowVersion(string value, out byte[]? bytes)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    // After the commit; AuditLogService swallows its own persistence errors, so an audit failure never fails the usage.
    private Task LogAsync(
        int? userId, string userName, string action, int entityId, string recordRef, string description,
        IReadOnlyList<AuditLogDetailEntry> details, string? ipAddress, CancellationToken cancellationToken) =>
        _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = userId,
            UserName = userName,
            Module = Module,
            Action = action,
            EntityName = EntityName,
            EntityId = entityId,
            RecordRef = recordRef,
            Description = description.Length <= 1000 ? description : description[..1000],
            IpAddress = ipAddress,
            Details = details,
        }, cancellationToken);

    internal static SparePartUsageDto MapToDto(SparePartUsage u, IReadOnlyList<SparePartStockTransaction>? movements = null) => new()
    {
        SparePartUsageId = u.SparePartUsageId,
        UsageNo = u.UsageNo,
        UsageDate = u.UsageDate,
        SparePartId = u.SparePartId,
        SparePartCode = u.SparePart?.SparePartCode ?? string.Empty,
        SparePartName = u.SparePart?.SparePartName ?? string.Empty,
        Unit = u.SparePart?.Unit ?? string.Empty,
        Quantity = u.Quantity,
        MaintenanceType = SparePartUsageMaintenanceType.MaintenanceTypeOf(u.UsedFor),
        MachinePmId = u.MachinePmId,
        MoldPmId = u.MoldPmId,
        MaintenanceNo = u.MachinePm?.PmNo ?? u.MoldPm?.PmNo ?? u.ReferenceNo ?? string.Empty,
        MachineId = u.MachineId,
        MachineCode = u.Machine?.MachineCode,
        MachineName = u.Machine?.MachineName,
        MoldId = u.MoldId,
        MoldCode = u.Mold?.MoldCode,
        MoldName = u.Mold?.MoldName,
        UsedByEmployeeId = u.UsedByEmployeeId,
        UsedByEmployeeName = u.UsedByEmployee?.EmployeeName,
        UnitCostAtIssue = u.UnitCostAtIssue,
        UsageCost = u.UnitCostAtIssue is { } cost ? Math.Round(cost * u.Quantity, 2, MidpointRounding.AwayFromZero) : null,
        Remarks = u.Remarks,
        Status = u.Status,
        ReversedAt = u.ReversedAt,
        ReversedBy = u.ReversedBy,
        ReversalReason = u.ReversalReason,
        CreatedAt = u.CreatedAt,
        CreatedBy = u.CreatedBy,
        UpdatedAt = u.UpdatedAt,
        UpdatedBy = u.UpdatedBy,
        StockMovements = (movements ?? Array.Empty<SparePartStockTransaction>()).Select(m => new SparePartStockMovementDto
        {
            StockTransactionId = m.StockTransactionId,
            TransactionType = m.TransactionType,
            Quantity = m.Quantity,
            PreviousStock = m.PreviousStock,
            NewStock = m.NewStock,
            ReferenceNo = m.ReferenceNo,
            TransactionAt = m.TransactionAt,
            CreatedBy = m.CreatedBy,
            Remarks = m.Remarks,
        }).ToList(),
        RowVersion = Convert.ToBase64String(u.RowVersion),
    };
}
