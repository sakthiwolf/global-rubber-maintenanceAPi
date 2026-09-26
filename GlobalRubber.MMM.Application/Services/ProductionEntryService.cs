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
/// Production Entry - the only source of mold usage (analysis 6.1). Rules are exactly those the analysis (6.1, 8.2, 8.3,
/// BR-08 to BR-13, the validation table) and the database define:
/// date, shift (Shift A/B/C), machine, product, mold and production qty are required; production qty &gt; 0; rejected qty
/// defaults to 0 and is 0..production qty; remarks at most 500; the machine and product must be active; the mold must
/// belong to the product; the mold's usage BEFORE the entry must be below its replacement shots; saving adds production
/// qty to the mold's usage, stores usage before/after, and re-evaluates the mold status. In the same transaction the mold
/// is evaluated for usage-based PM (<see cref="IMoldPmEvaluator"/>: warning / automatic PM). Good qty is computed by SQL
/// Server. No date limit, no duplicate date+shift+machine+mold rule, no edit or cancel - all open in Q-14.
/// </summary>
public sealed class ProductionEntryService : IProductionEntryService
{
    private const string ProductionModule = "Production Entry"; // audit "Module" value: the menu name
    private const int RemarksMaxLength = 500;                     // remarks NVARCHAR(500)

    /// <summary>The template's own message (analysis BR-10 / validation table).</summary>
    public const string ReplacementLimitMessage =
        "This mold has reached its replacement limit and cannot be used for production. Please select another mold.";

    /// <summary>The analysis' message (validation table).</summary>
    public const string MoldProductMismatchMessage = "Selected mold does not belong to the product.";

    private readonly IProductionEntryRepository _repository;
    private readonly IMachineRepository _machineRepository;
    private readonly IProductRepository _productRepository;
    private readonly IMoldRepository _moldRepository;
    private readonly IMoldPmEvaluator _moldPmEvaluator;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ProductionEntryService> _logger;

    public ProductionEntryService(
        IProductionEntryRepository repository,
        IMachineRepository machineRepository,
        IProductRepository productRepository,
        IMoldRepository moldRepository,
        IMoldPmEvaluator moldPmEvaluator,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<ProductionEntryService> logger)
    {
        _repository = repository;
        _machineRepository = machineRepository;
        _productRepository = productRepository;
        _moldRepository = moldRepository;
        _moldPmEvaluator = moldPmEvaluator;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<ProductionEntryDto>> GetAllAsync(ProductionEntryListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, cancellationToken);

        return PagedResult<ProductionEntryDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<ProductionEntryDto> GetByIdAsync(int productionEntryId, CancellationToken cancellationToken)
    {
        var entry = await _repository.GetByIdAsync(productionEntryId, cancellationToken)
            ?? throw new NotFoundException(nameof(ProductionEntry), productionEntryId);

        return MapToDto(entry);
    }

    public Task<ProductionLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    public async Task<ProductionEntryDto> CreateAsync(
        CreateProductionEntryRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var input = NormalizeAndValidate(request);

        // References (BR-13 and the validation table): unknown -> 404, inactive / wrong product -> 400, as for the masters.
        var machine = await _machineRepository.GetByIdAsync(input.MachineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), input.MachineId);
        if (!machine.IsActive)
        {
            throw new ValidationException("The selected machine is not active.");
        }

        var product = await _productRepository.GetByIdAsync(input.ProductId, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), input.ProductId);
        if (!product.IsActive)
        {
            throw new ValidationException("The selected product is not active.");
        }

        // Existence check up front for a clean 404; the limit and the mold/product match are (re)checked on the LOCKED
        // row inside the save, which is what actually decides.
        _ = await _moldRepository.GetByIdAsync(input.MoldId, cancellationToken)
            ?? throw new NotFoundException(nameof(Mold), input.MoldId);

        var now = _dateTimeProvider.UtcNow;
        var entry = new ProductionEntry
        {
            // The id / entry number / good qty / usage snapshot are never client-controlled.
            EntryDate = input.EntryDate,
            Shift = input.Shift,
            MachineId = input.MachineId,
            ProductId = input.ProductId,
            MoldId = input.MoldId,
            ProductionQty = input.ProductionQty,
            RejectedQty = input.RejectedQty,
            Remarks = input.Remarks,
            Status = ProductionEntryStatus.Saved, // the only status ever written (Q-14)
            CreatedAt = now,
            CreatedBy = actingUserId,
        };

        string moldStatusBefore = string.Empty;
        string moldStatusAfter = string.Empty;
        var evaluation = MoldPmEvaluation.Nothing;

        var created = await _repository.AddAsync(entry, mold =>
        {
            if (mold.ProductId != input.ProductId)
            {
                throw new ValidationException(MoldProductMismatchMessage);
            }

            // BR-10: only the usage BEFORE the entry is checked (Q-13: one entry may overshoot the limit).
            if (!MoldLifeEngine.IsProductionAllowed(mold.CurrentUsageShots, mold.ReplacementShots))
            {
                throw new ConflictException(ReplacementLimitMessage);
            }

            // BR-11: usage += production qty (pieces 1:1, cavities ignored - Q-01).
            var usageAfter = (long)mold.CurrentUsageShots + input.ProductionQty;
            if (usageAfter > int.MaxValue)
            {
                throw new ValidationException("ProductionQty would take the mold's usage beyond the largest value that can be stored.");
            }

            entry.MoldUsageBefore = mold.CurrentUsageShots;
            entry.MoldUsageAfter = (int)usageAfter;

            // BR-12 / 8.3.
            moldStatusBefore = mold.Status;
            moldStatusAfter = MoldLifeEngine.StatusAfterProduction(mold.Status, entry.MoldUsageAfter, mold.WarningShots, mold.ReplacementShots);

            mold.CurrentUsageShots = entry.MoldUsageAfter;
            mold.Status = moldStatusAfter;
            mold.UpdatedAt = now;
            mold.UpdatedBy = actingUserId;
        }, async (savedMold, ct) => evaluation = await _moldPmEvaluator.EvaluateUsageAsync(savedMold, ct), cancellationToken);

        created.Machine = machine;
        created.Product = product;

        // Invariant culture (as the other services): the audit text must not depend on the server's regional settings.
        static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string Grouped(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        var details = new List<AuditLogDetailEntry>
        {
            new("mold_usage", Num(created.MoldUsageBefore), Num(created.MoldUsageAfter)),
        };
        if (!string.Equals(moldStatusBefore, moldStatusAfter, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("mold_status", moldStatusBefore, moldStatusAfter));
        }

        // The template's own description ("... Mold usage updated from X to Y.") with the mold identified.
        await WriteAuditAsync(
            "ProductionEntryCreated", created, actingUserId, ipAddress,
            actor => $"{actor} saved production entry {created.EntryNo} for {Grouped(created.ProductionQty)} pcs on mold {created.Mold.MoldCode}. " +
                     $"Mold usage updated from {Grouped(created.MoldUsageBefore)} to {Grouped(created.MoldUsageAfter)}." +
                     (moldStatusBefore != moldStatusAfter ? $" Mold status changed from {moldStatusBefore} to {moldStatusAfter}." : string.Empty),
            details, cancellationToken);

        var actorName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);
        await _moldPmEvaluator.WriteAuditAsync(evaluation, $"production entry {created.EntryNo} by {actorName}", ipAddress, cancellationToken);

        return MapToDto(created);
    }

    private sealed record ValidInput(
        DateOnly EntryDate, string Shift, int MachineId, int ProductId, int MoldId, int ProductionQty, int RejectedQty, string? Remarks);

    // One place for every field rule, so all problems are reported together; the shift is matched case-insensitively
    // onto the exact CK value; remarks are trimmed and blank becomes null.
    private static ValidInput NormalizeAndValidate(CreateProductionEntryRequest request)
    {
        var shiftInput = string.IsNullOrWhiteSpace(request.Shift) ? null : request.Shift.Trim();
        var shift = ProductionShift.All.FirstOrDefault(s => string.Equals(s, shiftInput, StringComparison.OrdinalIgnoreCase));
        var remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        var rejected = request.RejectedQty ?? 0; // analysis 6.1: optional, default 0

        var errors = new List<string>();

        if (request.EntryDate is null) errors.Add("EntryDate is required.");

        if (shiftInput is null) errors.Add("Shift is required.");
        else if (shift is null) errors.Add($"Shift must be one of: {string.Join(", ", ProductionShift.All)}.");

        if (request.MachineId is null or <= 0) errors.Add("MachineId is required.");
        if (request.ProductId is null or <= 0) errors.Add("ProductId is required.");
        if (request.MoldId is null or <= 0) errors.Add("MoldId is required.");

        if (request.ProductionQty is null) errors.Add("ProductionQty is required.");
        else if (request.ProductionQty <= 0) errors.Add("Production quantity must be greater than 0.");

        if (rejected < 0) errors.Add("Rejected quantity cannot be negative.");
        else if (request.ProductionQty is > 0 && rejected > request.ProductionQty) errors.Add("Rejected quantity cannot be more than production quantity.");

        if (remarks is { Length: > RemarksMaxLength }) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new ValidInput(request.EntryDate!.Value, shift!, request.MachineId!.Value, request.ProductId!.Value, request.MoldId!.Value,
            request.ProductionQty!.Value, rejected, remarks);
    }

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, ProductionEntry entry, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = ProductionModule,
            Action = action,
            EntityName = "ProductionEntry",
            EntityId = entry.ProductionEntryId,
            RecordRef = entry.EntryNo,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static ProductionEntryDto MapToDto(ProductionEntry entry) => new()
    {
        ProductionEntryId = entry.ProductionEntryId,
        EntryNo = entry.EntryNo,
        EntryDate = entry.EntryDate,
        Shift = entry.Shift,
        MachineId = entry.MachineId,
        MachineCode = entry.Machine?.MachineCode ?? string.Empty,
        MachineName = entry.Machine?.MachineName ?? string.Empty,
        ProductId = entry.ProductId,
        ProductCode = entry.Product?.ProductCode ?? string.Empty,
        ProductName = entry.Product?.ProductName ?? string.Empty,
        MoldId = entry.MoldId,
        MoldCode = entry.Mold?.MoldCode ?? string.Empty,
        MoldName = entry.Mold?.MoldName ?? string.Empty,
        ProductionQty = entry.ProductionQty,
        RejectedQty = entry.RejectedQty,
        GoodQty = entry.GoodQty ?? entry.ProductionQty - entry.RejectedQty,
        MoldUsageBefore = entry.MoldUsageBefore,
        MoldUsageAfter = entry.MoldUsageAfter,
        Remarks = entry.Remarks,
        Status = entry.Status,
        CreatedAt = entry.CreatedAt,
        CreatedBy = entry.CreatedBy,
        UpdatedAt = entry.UpdatedAt,
        UpdatedBy = entry.UpdatedBy,
        RowVersion = Convert.ToBase64String(entry.RowVersion),
    };
}
