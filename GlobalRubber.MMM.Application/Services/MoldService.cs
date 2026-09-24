using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Mold master. Rules are exactly those the analysis (section 4.2, 8.2, BR-06/BR-07/BR-14, the validation table) and the
/// database define:
/// - name, product, mold type, maximum/warning/replacement shots and status required; column lengths from the table;
/// - CK constraints: cavities &gt;= 1; max &gt; 0; warning &lt; max; replacement &gt; warning and &lt;= max; usage &gt;= 0;
///   status one of the five values (the template's own messages where it has them);
/// - product must exist and, when newly assigned, be active; the responsible person is optional but, when newly
///   assigned, must exist and be active - an unchanged inactive one is kept (the convention of the other masters);
/// - serial number unique whenever entered (UX_mold_master_serial_number);
/// - name unique among molds that are not Retired (the all-masters "unique among active records" rule - Q-27 open -
///   with Retired as the mold's inactive state);
/// - a new mold starts at 0 usage; usage can be corrected on edit only (BR-14) and is always audited with old/new values;
/// - status: any of the five values on create and edit, as in the template (which values may be set manually is open
///   question Q-09). "Deactivate" (DELETE) sets Status = Retired - the table has no is_active column.
/// Life state / life % / remaining shots follow section 8.2.
/// </summary>
public sealed class MoldService : IMoldService
{
    private const string MoldModule = "Mold"; // audit "Module" value: the menu name, as for the other masters
    private const int NameMaxLength = 150;            // mold_name NVARCHAR(150)
    private const int TypeMaxLength = 100;            // mold_type NVARCHAR(100)
    private const int ManufacturerMaxLength = 100;    // manufacturer NVARCHAR(100)
    private const int SerialNumberMaxLength = 100;    // serial_number NVARCHAR(100)
    private const int LocationMaxLength = 100;        // location NVARCHAR(100)
    private const int StorageLocationMaxLength = 100; // storage_location NVARCHAR(100)
    private const int RemarksMaxLength = 500;         // remarks NVARCHAR(500)

    private readonly IMoldRepository _moldRepository;
    private readonly IProductRepository _productRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MoldService> _logger;

    public MoldService(
        IMoldRepository moldRepository,
        IProductRepository productRepository,
        IEmployeeRepository employeeRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MoldService> logger)
    {
        _moldRepository = moldRepository;
        _productRepository = productRepository;
        _employeeRepository = employeeRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MoldDto>> GetAllAsync(MoldListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _moldRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<MoldDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<MoldDto> GetByIdAsync(int moldId, CancellationToken cancellationToken)
    {
        var mold = await _moldRepository.GetByIdAsync(moldId, cancellationToken)
            ?? throw new NotFoundException(nameof(Mold), moldId);

        return MapToDto(mold);
    }

    public async Task<MoldDto> CreateAsync(
        CreateMoldRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(request, currentUsage: 0, rowVersion: null, requireRowVersion: false, out _);

        var product = await LoadAssignableProductAsync(fields.ProductId, cancellationToken);
        var person = fields.ResponsibleEmployeeId is { } personId
            ? await LoadAssignablePersonAsync(personId, cancellationToken)
            : null;

        await EnsureUniqueAsync(fields, excludeMoldId: null, cancellationToken);

        var mold = new Mold
        {
            // MoldId / MoldCode are never client-controlled: the code is issued by the repository from the MOLD document
            // sequence inside the insert's transaction. A new mold starts at 0 shots.
            MoldName = fields.Name,
            ProductId = product.ProductId,
            MoldType = fields.MoldType,
            CavityCount = fields.CavityCount,
            Manufacturer = fields.Manufacturer,
            SerialNumber = fields.SerialNumber,
            Location = fields.Location,
            StorageLocation = fields.StorageLocation,
            CommissionDate = fields.CommissionDate,
            MaximumShots = fields.MaximumShots,
            WarningShots = fields.WarningShots,
            ReplacementShots = fields.ReplacementShots,
            MaintenanceFrequencyShots = fields.MaintenanceFrequencyShots,
            CurrentUsageShots = 0,
            ResponsibleEmployeeId = person?.EmployeeId,
            Status = fields.Status,
            Remarks = fields.Remarks,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _moldRepository.AddAsync(mold, cancellationToken);
        created.Product = product; // set after the save so EF never tries to insert the referenced rows
        created.ResponsibleEmployee = person;

        await WriteAuditAsync(
            "MoldCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created mold '{created.MoldName}' ({created.MoldCode}) for product {product.ProductCode}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<MoldDto> UpdateAsync(
        int moldId, UpdateMoldRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var mold = await _moldRepository.GetByIdAsync(moldId, cancellationToken)
            ?? throw new NotFoundException(nameof(Mold), moldId);

        var fields = NormalizeAndValidate(
            request, request.CurrentUsageShots ?? mold.CurrentUsageShots, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        // Keeping the current product / person is always allowed (even if since deactivated - the form shows it flagged
        // instead of silently clearing it); choosing a different one requires an existing, active one.
        var oldProduct = mold.Product;
        var product = fields.ProductId != mold.ProductId
            ? await LoadAssignableProductAsync(fields.ProductId, cancellationToken)
            : oldProduct;

        var oldPerson = mold.ResponsibleEmployee;
        var person = fields.ResponsibleEmployeeId == mold.ResponsibleEmployeeId
            ? oldPerson
            : fields.ResponsibleEmployeeId is { } personId
                ? await LoadAssignablePersonAsync(personId, cancellationToken)
                : null;

        await EnsureUniqueAsync(fields, moldId, cancellationToken);

        // Mold data carries nothing personal or secret; the responsible person is recorded by employee code only. The
        // usage correction (BR-14) is recorded with its old and new value like every other field.
        var details = new List<AuditLogDetailEntry>();
        void Track(string field, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) details.Add(new AuditLogDetailEntry(field, oldValue, newValue));
        }

        static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string? NumOrNull(int? value) => value?.ToString(CultureInfo.InvariantCulture);

        Track("mold_name", mold.MoldName, fields.Name);
        Track("product_code", oldProduct?.ProductCode, product.ProductCode);
        Track("mold_type", mold.MoldType, fields.MoldType);
        Track("cavity_count", Num(mold.CavityCount), Num(fields.CavityCount));
        Track("manufacturer", mold.Manufacturer, fields.Manufacturer);
        Track("serial_number", mold.SerialNumber, fields.SerialNumber);
        Track("location", mold.Location, fields.Location);
        Track("storage_location", mold.StorageLocation, fields.StorageLocation);
        Track("commission_date", mold.CommissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fields.CommissionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Track("maximum_shots", Num(mold.MaximumShots), Num(fields.MaximumShots));
        Track("warning_shots", Num(mold.WarningShots), Num(fields.WarningShots));
        Track("replacement_shots", Num(mold.ReplacementShots), Num(fields.ReplacementShots));
        Track("maintenance_frequency_shots", NumOrNull(mold.MaintenanceFrequencyShots), NumOrNull(fields.MaintenanceFrequencyShots));
        Track("current_usage_shots", Num(mold.CurrentUsageShots), Num(fields.CurrentUsageShots));
        Track("responsible_employee_code", oldPerson?.EmployeeCode, person?.EmployeeCode);
        Track("status", mold.Status, fields.Status);
        Track("remarks", mold.Remarks, fields.Remarks);

        mold.MoldName = fields.Name;
        mold.ProductId = product.ProductId;
        mold.MoldType = fields.MoldType;
        mold.CavityCount = fields.CavityCount;
        mold.Manufacturer = fields.Manufacturer;
        mold.SerialNumber = fields.SerialNumber;
        mold.Location = fields.Location;
        mold.StorageLocation = fields.StorageLocation;
        mold.CommissionDate = fields.CommissionDate;
        mold.MaximumShots = fields.MaximumShots;
        mold.WarningShots = fields.WarningShots;
        mold.ReplacementShots = fields.ReplacementShots;
        mold.MaintenanceFrequencyShots = fields.MaintenanceFrequencyShots;
        mold.CurrentUsageShots = fields.CurrentUsageShots;
        mold.ResponsibleEmployeeId = person?.EmployeeId;
        mold.Status = fields.Status;
        mold.Remarks = fields.Remarks;
        mold.UpdatedAt = _dateTimeProvider.UtcNow;
        mold.UpdatedBy = actingUserId;

        var updated = await _moldRepository.UpdateAsync(mold, originalRowVersion!, cancellationToken);
        updated.Product = product;
        updated.ResponsibleEmployee = person;

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "MoldUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated mold '{updated.MoldName}' ({updated.MoldCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<MoldDto> DeactivateAsync(
        int moldId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var mold = await _moldRepository.GetByIdAsync(moldId, cancellationToken)
            ?? throw new NotFoundException(nameof(Mold), moldId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (mold.Status == MoldStatus.Retired)
        {
            throw new ConflictException("The mold is already retired.");
        }

        var oldStatus = mold.Status;
        mold.Status = MoldStatus.Retired;
        mold.UpdatedAt = _dateTimeProvider.UtcNow;
        mold.UpdatedBy = actingUserId;

        var retired = await _moldRepository.RetireAsync(mold, cancellationToken);

        await WriteAuditAsync(
            "MoldDeactivated", retired, actingUserId, ipAddress,
            actor => $"{actor} retired mold '{retired.MoldName}' ({retired.MoldCode}).",
            new[] { new AuditLogDetailEntry("status", oldStatus, MoldStatus.Retired) }, cancellationToken);

        return MapToDto(retired);
    }

    // The selected product must exist (404) and be active (400) - the same shape as Machine's department check.
    private async Task<Product> LoadAssignableProductAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), productId);

        if (!product.IsActive)
        {
            throw new ValidationException("The selected product is not active.");
        }

        return product;
    }

    private async Task<Employee> LoadAssignablePersonAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        if (!employee.IsActive)
        {
            throw new ValidationException("The selected responsible person is not active.");
        }

        return employee;
    }

    private async Task EnsureUniqueAsync(MoldFields fields, int? excludeMoldId, CancellationToken cancellationToken)
    {
        if (fields.Status != MoldStatus.Retired
            && await _moldRepository.ExistsNonRetiredByNameAsync(fields.Name, excludeMoldId, cancellationToken))
        {
            throw new ConflictException($"A mold named '{fields.Name}' already exists.");
        }

        if (fields.SerialNumber is not null
            && await _moldRepository.ExistsBySerialNumberAsync(fields.SerialNumber, excludeMoldId, cancellationToken))
        {
            throw new ConflictException($"A mold with serial number '{fields.SerialNumber}' already exists.");
        }
    }

    private sealed record MoldFields(
        string Name, int ProductId, string MoldType, int CavityCount, string? Manufacturer, string? SerialNumber,
        string? Location, string? StorageLocation, DateOnly? CommissionDate, int MaximumShots, int WarningShots,
        int ReplacementShots, int? MaintenanceFrequencyShots, int CurrentUsageShots, int? ResponsibleEmployeeId,
        string Status, string? Remarks);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about what a
    // valid mold looks like; the row version is only checked (and decoded) for Update.
    private static MoldFields NormalizeAndValidate(
        CreateMoldRequest request, int currentUsage, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var name = (request.MoldName ?? string.Empty).Trim();
        var moldType = (request.MoldType ?? string.Empty).Trim();
        var manufacturer = Optional(request.Manufacturer);
        var serialNumber = Optional(request.SerialNumber);
        var location = Optional(request.Location);
        var storageLocation = Optional(request.StorageLocation);
        var remarks = Optional(request.Remarks);
        var cavityCount = request.CavityCount ?? 1; // template / DF_mold_master_cavity_count default
        var statusInput = Optional(request.Status);
        // Case-insensitive match onto the exact CK value, so "retired" is stored as "Retired".
        var status = MoldStatus.All.FirstOrDefault(s => string.Equals(s, statusInput, StringComparison.OrdinalIgnoreCase));

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

        RequiredText(name, NameMaxLength, "MoldName");
        if (request.ProductId <= 0) errors.Add("ProductId is required.");
        RequiredText(moldType, TypeMaxLength, "MoldType");
        MaxLength(manufacturer, ManufacturerMaxLength, "Manufacturer");
        MaxLength(serialNumber, SerialNumberMaxLength, "SerialNumber");
        MaxLength(location, LocationMaxLength, "Location");
        MaxLength(storageLocation, StorageLocationMaxLength, "StorageLocation");
        MaxLength(remarks, RemarksMaxLength, "Remarks");

        if (cavityCount < 1) errors.Add("CavityCount must be at least 1."); // CK_mold_master_cavity_count

        // Life thresholds: the three CK constraints, with the template's messages (analysis 4.2 / validation table).
        if (request.MaximumShots is null) errors.Add("MaximumShots is required.");
        if (request.WarningShots is null) errors.Add("WarningShots is required.");
        if (request.ReplacementShots is null) errors.Add("ReplacementShots is required.");
        if (request.MaximumShots is { } max && max <= 0) errors.Add("Maximum shots must be greater than 0.");
        if (request is { MaximumShots: { } m, WarningShots: { } w } && w >= m) errors.Add("Warning level must be less than maximum life.");
        if (request is { WarningShots: { } w2, ReplacementShots: { } r } && r <= w2) errors.Add("Replacement level must be greater than warning level.");
        if (request is { MaximumShots: { } m2, ReplacementShots: { } r2 } && r2 > m2) errors.Add("Replacement level cannot exceed maximum shots.");

        if (currentUsage < 0) errors.Add("CurrentUsageShots cannot be negative."); // CK_mold_master_current_usage_shots

        if (statusInput is null) errors.Add("Status is required.");
        else if (status is null) errors.Add($"Status must be one of: {string.Join(", ", MoldStatus.All)}.");

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

        return new MoldFields(
            name, request.ProductId, moldType, cavityCount, manufacturer, serialNumber, location, storageLocation,
            request.CommissionDate, request.MaximumShots!.Value, request.WarningShots!.Value, request.ReplacementShots!.Value,
            request.MaintenanceFrequencyShots, currentUsage, request.ResponsibleEmployeeId, status!, remarks);
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

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, Mold mold, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MoldModule,
            Action = action,
            EntityName = "Mold",
            EntityId = mold.MoldId,
            RecordRef = mold.MoldCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    // Analysis 8.2: LifeUsed% = MIN(100, ROUND(CurrentUsage / MaxShots * 100)); Remaining = MAX(0, MaxShots - CurrentUsage).
    public static int LifeUsedPercent(int currentUsage, int maximumShots) =>
        maximumShots <= 0
            ? 0
            : (int)Math.Min(100m, Math.Round(currentUsage * 100m / maximumShots, MidpointRounding.AwayFromZero));

    private static MoldDto MapToDto(Mold mold) => new()
    {
        MoldId = mold.MoldId,
        MoldCode = mold.MoldCode,
        MoldName = mold.MoldName,
        ProductId = mold.ProductId,
        ProductCode = mold.Product?.ProductCode ?? string.Empty,
        ProductName = mold.Product?.ProductName ?? string.Empty,
        ProductIsActive = mold.Product?.IsActive ?? false,
        MoldType = mold.MoldType,
        CavityCount = mold.CavityCount,
        Manufacturer = mold.Manufacturer,
        SerialNumber = mold.SerialNumber,
        Location = mold.Location,
        StorageLocation = mold.StorageLocation,
        CommissionDate = mold.CommissionDate,
        MaximumShots = mold.MaximumShots,
        WarningShots = mold.WarningShots,
        ReplacementShots = mold.ReplacementShots,
        MaintenanceFrequencyShots = mold.MaintenanceFrequencyShots,
        CurrentUsageShots = mold.CurrentUsageShots,
        ResponsibleEmployeeId = mold.ResponsibleEmployeeId,
        ResponsibleEmployeeName = mold.ResponsibleEmployee?.EmployeeName,
        ResponsibleEmployeeIsActive = mold.ResponsibleEmployee?.IsActive,
        Status = mold.Status,
        LifeState = mold.LifeState,
        LifeUsedPercent = LifeUsedPercent(mold.CurrentUsageShots, mold.MaximumShots),
        RemainingShots = Math.Max(0, mold.MaximumShots - mold.CurrentUsageShots),
        Remarks = mold.Remarks,
        CreatedAt = mold.CreatedAt,
        CreatedBy = mold.CreatedBy,
        UpdatedAt = mold.UpdatedAt,
        UpdatedBy = mold.UpdatedBy,
        RowVersion = Convert.ToBase64String(mold.RowVersion),
    };
}
