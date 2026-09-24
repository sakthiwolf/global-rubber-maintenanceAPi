using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Spare Part master. Rules are exactly those the analysis (section 4.7, BR-29, the validation table) and the database
/// define:
/// - name, unit, minimum stock and current stock required; column lengths from the table;
/// - minimum and current stock &gt;= 0 with 0 allowed (CK constraints; the analysis fixes template defect D-04); no
///   relationship between the two is enforced - their comparison only drives the computed stock status (BR-29);
/// - unit cost optional, &gt;= 0 (CK), DECIMAL(18,2);
/// - linked machine and supplier optional; when newly chosen they must exist and be active - an unchanged inactive one is
///   kept (the convention of the other masters);
/// - current stock is entered and edited directly in the master (4.7 "Editable directly", 10.4, the ownership table:
///   "Master edit") - open question Q-06 asks whether that should remain; every change is audited with old/new values;
/// - name unique among active spare parts (the all-masters "unique among active records" rule - Q-27 open);
/// - stock status is the persisted computed column - read back from SQL Server, never written.
/// </summary>
public sealed class SparePartService : ISparePartService
{
    private const string SparePartModule = "Spare Part"; // audit "Module" value: the menu name, as for the other masters
    private const int NameMaxLength = 150;          // spare_part_name NVARCHAR(150)
    private const int CategoryMaxLength = 100;      // category NVARCHAR(100)
    private const int PartNumberMaxLength = 50;     // part_number NVARCHAR(50)
    private const int UnitMaxLength = 20;           // unit NVARCHAR(20)
    private const int StoreLocationMaxLength = 100; // store_location NVARCHAR(100)
    private const decimal UnitCostMax = 9_999_999_999_999_999.99m; // DECIMAL(18,2)

    private readonly ISparePartRepository _sparePartRepository;
    private readonly IMachineRepository _machineRepository;
    private readonly IVendorRepository _vendorRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SparePartService> _logger;

    public SparePartService(
        ISparePartRepository sparePartRepository,
        IMachineRepository machineRepository,
        IVendorRepository vendorRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<SparePartService> logger)
    {
        _sparePartRepository = sparePartRepository;
        _machineRepository = machineRepository;
        _vendorRepository = vendorRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<SparePartDto>> GetAllAsync(SparePartListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _sparePartRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<SparePartDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<SparePartDto> GetByIdAsync(int sparePartId, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartRepository.GetByIdAsync(sparePartId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePart), sparePartId);

        return MapToDto(sparePart);
    }

    public async Task<SparePartDto> CreateAsync(
        CreateSparePartRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(request, rowVersion: null, requireRowVersion: false, out _);

        var machine = fields.MachineId is { } machineId ? await LoadAssignableMachineAsync(machineId, cancellationToken) : null;
        var vendor = fields.VendorId is { } vendorId ? await LoadAssignableVendorAsync(vendorId, cancellationToken) : null;

        await EnsureUniqueNameAsync(fields.Name, excludeSparePartId: null, cancellationToken);

        var sparePart = new SparePart
        {
            // SparePartId / SparePartCode are never client-controlled: the code is issued by the repository from the
            // SPARE_PART document sequence inside the insert's transaction. StockStatus is computed by SQL Server.
            SparePartName = fields.Name,
            Category = fields.Category,
            MachineId = machine?.MachineId,
            PartNumber = fields.PartNumber,
            Unit = fields.Unit,
            MinimumStock = fields.MinimumStock,
            CurrentStock = fields.CurrentStock,
            VendorId = vendor?.VendorId,
            StoreLocation = fields.StoreLocation,
            UnitCost = fields.UnitCost,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _sparePartRepository.AddAsync(sparePart, cancellationToken);
        created.Machine = machine; // set after the save so EF never tries to insert the referenced rows
        created.Vendor = vendor;

        await WriteAuditAsync(
            "SparePartCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created spare part '{created.SparePartName}' ({created.SparePartCode}) with current stock {Num(created.CurrentStock)} {created.Unit}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<SparePartDto> UpdateAsync(
        int sparePartId, UpdateSparePartRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var sparePart = await _sparePartRepository.GetByIdAsync(sparePartId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePart), sparePartId);

        var fields = NormalizeAndValidate(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        // Keeping the current machine / supplier is always allowed (even if since deactivated - the form shows it flagged
        // instead of silently clearing it); choosing a different one requires an existing, active one.
        var oldMachine = sparePart.Machine;
        var machine = fields.MachineId == sparePart.MachineId
            ? oldMachine
            : fields.MachineId is { } machineId ? await LoadAssignableMachineAsync(machineId, cancellationToken) : null;

        var oldVendor = sparePart.Vendor;
        var vendor = fields.VendorId == sparePart.VendorId
            ? oldVendor
            : fields.VendorId is { } vendorId ? await LoadAssignableVendorAsync(vendorId, cancellationToken) : null;

        await EnsureUniqueNameAsync(fields.Name, sparePartId, cancellationToken);

        // Spare part data carries nothing personal or secret. The machine is recorded by code and the supplier by name;
        // the current stock correction (Q-06) is recorded with its old and new value like every other field.
        var details = new List<AuditLogDetailEntry>();
        void Track(string field, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) details.Add(new AuditLogDetailEntry(field, oldValue, newValue));
        }

        Track("spare_part_name", sparePart.SparePartName, fields.Name);
        Track("category", sparePart.Category, fields.Category);
        Track("machine_code", oldMachine?.MachineCode, machine?.MachineCode);
        Track("part_number", sparePart.PartNumber, fields.PartNumber);
        Track("unit", sparePart.Unit, fields.Unit);
        Track("minimum_stock", Num(sparePart.MinimumStock), Num(fields.MinimumStock));
        Track("current_stock", Num(sparePart.CurrentStock), Num(fields.CurrentStock));
        Track("vendor_name", oldVendor?.VendorName, vendor?.VendorName);
        Track("store_location", sparePart.StoreLocation, fields.StoreLocation);
        Track("unit_cost", Money(sparePart.UnitCost), Money(fields.UnitCost));

        var oldStockStatus = sparePart.StockStatus;

        sparePart.SparePartName = fields.Name;
        sparePart.Category = fields.Category;
        sparePart.MachineId = machine?.MachineId;
        sparePart.PartNumber = fields.PartNumber;
        sparePart.Unit = fields.Unit;
        sparePart.MinimumStock = fields.MinimumStock;
        sparePart.CurrentStock = fields.CurrentStock;
        sparePart.VendorId = vendor?.VendorId;
        sparePart.StoreLocation = fields.StoreLocation;
        sparePart.UnitCost = fields.UnitCost;
        sparePart.UpdatedAt = _dateTimeProvider.UtcNow;
        sparePart.UpdatedBy = actingUserId;

        var updated = await _sparePartRepository.UpdateAsync(sparePart, originalRowVersion!, cancellationToken);
        updated.Machine = machine;
        updated.Vendor = vendor;

        // The status itself is SQL Server's; recording its resulting change makes Low/Out-of-stock transitions visible.
        Track("stock_status", oldStockStatus, updated.StockStatus);

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "SparePartUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated spare part '{updated.SparePartName}' ({updated.SparePartCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<SparePartDto> DeactivateAsync(
        int sparePartId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var sparePart = await _sparePartRepository.GetByIdAsync(sparePartId, cancellationToken)
            ?? throw new NotFoundException(nameof(SparePart), sparePartId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (!sparePart.IsActive)
        {
            throw new ConflictException("The spare part is already inactive.");
        }

        sparePart.IsActive = false;
        sparePart.UpdatedAt = _dateTimeProvider.UtcNow;
        sparePart.UpdatedBy = actingUserId;

        var deactivated = await _sparePartRepository.DeactivateAsync(sparePart, cancellationToken);

        await WriteAuditAsync(
            "SparePartDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated spare part '{deactivated.SparePartName}' ({deactivated.SparePartCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    // The selected machine / supplier must exist (404) and be active (400) - the same shape as the other masters' checks.
    private async Task<Machine> LoadAssignableMachineAsync(int machineId, CancellationToken cancellationToken)
    {
        var machine = await _machineRepository.GetByIdAsync(machineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), machineId);

        if (!machine.IsActive)
        {
            throw new ValidationException("The selected machine is not active.");
        }

        return machine;
    }

    private async Task<Vendor> LoadAssignableVendorAsync(int vendorId, CancellationToken cancellationToken)
    {
        var vendor = await _vendorRepository.GetByIdAsync(vendorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vendor), vendorId);

        if (!vendor.IsActive)
        {
            throw new ValidationException("The selected supplier is not active.");
        }

        return vendor;
    }

    private async Task EnsureUniqueNameAsync(string name, int? excludeSparePartId, CancellationToken cancellationToken)
    {
        if (await _sparePartRepository.ExistsActiveByNameAsync(name, excludeSparePartId, cancellationToken))
        {
            throw new ConflictException($"A spare part named '{name}' already exists.");
        }
    }

    private sealed record SparePartFields(
        string Name, string? Category, int? MachineId, string? PartNumber, string Unit, int MinimumStock, int CurrentStock,
        int? VendorId, string? StoreLocation, decimal? UnitCost);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about what a
    // valid spare part looks like; the row version is only checked (and decoded) for Update.
    private static SparePartFields NormalizeAndValidate(
        CreateSparePartRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var name = (request.SparePartName ?? string.Empty).Trim();
        var unit = (request.Unit ?? string.Empty).Trim();
        var category = Optional(request.Category);
        var partNumber = Optional(request.PartNumber);
        var storeLocation = Optional(request.StoreLocation);

        var errors = new List<string>();

        void RequiredText(string value, int max, string field)
        {
            if (value.Length == 0) errors.Add($"{field} is required.");
            else if (value.Length > max) errors.Add($"{field} must be at most {max} characters.");
        }

        void MaxLength(string? value, int max, string field)
        {
            if (value is not null && value.Length > max) errors.Add($"{field} must be at most {max} characters.");
        }

        RequiredText(name, NameMaxLength, "SparePartName");
        MaxLength(category, CategoryMaxLength, "Category");
        MaxLength(partNumber, PartNumberMaxLength, "PartNumber");
        RequiredText(unit, UnitMaxLength, "Unit");
        MaxLength(storeLocation, StoreLocationMaxLength, "StoreLocation");

        // Required means "present", not "non-zero": 0 is a valid stock level (validation table; fixes D-04).
        if (request.MinimumStock is null) errors.Add("MinimumStock is required.");
        else if (request.MinimumStock < 0) errors.Add("MinimumStock cannot be negative."); // CK_spare_part_master_minimum_stock
        if (request.CurrentStock is null) errors.Add("CurrentStock is required.");
        else if (request.CurrentStock < 0) errors.Add("CurrentStock cannot be negative."); // CK_spare_part_master_current_stock

        if (request.MachineId is <= 0) errors.Add("MachineId is not valid.");
        if (request.VendorId is <= 0) errors.Add("VendorId is not valid.");

        if (request.UnitCost is { } cost)
        {
            if (cost < 0) errors.Add("UnitCost cannot be negative."); // CK_spare_part_master_unit_cost
            else if (cost > UnitCostMax) errors.Add("UnitCost is too large.");
            else if (decimal.Round(cost, 2) != cost) errors.Add("UnitCost can have at most 2 decimal places.");
        }

        originalRowVersion = null;
        if (requireRowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion)) errors.Add("RowVersion is required.");
            else if (!TryDecodeRowVersion(rowVersion, out originalRowVersion)) errors.Add("RowVersion is not valid.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new SparePartFields(
            name, category, request.MachineId, partNumber, unit, request.MinimumStock!.Value, request.CurrentStock!.Value,
            request.VendorId, storeLocation, request.UnitCost);
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

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string? Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, SparePart sparePart, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = SparePartModule,
            Action = action,
            EntityName = "SparePart",
            EntityId = sparePart.SparePartId,
            RecordRef = sparePart.SparePartCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static SparePartDto MapToDto(SparePart sparePart) => new()
    {
        SparePartId = sparePart.SparePartId,
        SparePartCode = sparePart.SparePartCode,
        SparePartName = sparePart.SparePartName,
        Category = sparePart.Category,
        MachineId = sparePart.MachineId,
        MachineCode = sparePart.Machine?.MachineCode,
        MachineName = sparePart.Machine?.MachineName,
        MachineIsActive = sparePart.Machine?.IsActive,
        PartNumber = sparePart.PartNumber,
        Unit = sparePart.Unit,
        MinimumStock = sparePart.MinimumStock,
        CurrentStock = sparePart.CurrentStock,
        VendorId = sparePart.VendorId,
        VendorName = sparePart.Vendor?.VendorName,
        VendorIsActive = sparePart.Vendor?.IsActive,
        StoreLocation = sparePart.StoreLocation,
        UnitCost = sparePart.UnitCost,
        StockStatus = sparePart.StockStatus,
        IsActive = sparePart.IsActive,
        CreatedAt = sparePart.CreatedAt,
        CreatedBy = sparePart.CreatedBy,
        UpdatedAt = sparePart.UpdatedAt,
        UpdatedBy = sparePart.UpdatedBy,
        RowVersion = Convert.ToBase64String(sparePart.RowVersion),
    };
}
