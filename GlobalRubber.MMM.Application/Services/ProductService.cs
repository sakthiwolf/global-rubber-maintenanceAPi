using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Product master. Rules are exactly those the analysis (section 4.3 and the validation table) and the database define:
/// name and unit of measure required; column lengths; standard cycle time optional but, when given, &gt; 0
/// (CK_product_master_standard_cycle_time_sec) and within DECIMAL(8,2); name unique among ACTIVE products (the
/// all-masters rule, applied the same way as Department/Vendor - Q-27 is still open).
/// </summary>
public sealed class ProductService : IProductService
{
    private const string ProductModule = "Product"; // audit "Module" value: the menu name, as for the other masters
    private const int NameMaxLength = 150;          // masters.product_master.product_name NVARCHAR(150)
    private const int CategoryMaxLength = 100;      // category NVARCHAR(100)
    private const int UnitOfMeasureMaxLength = 20;  // unit_of_measure NVARCHAR(20)
    private const int RemarksMaxLength = 500;       // remarks NVARCHAR(500)
    private const decimal CycleTimeMax = 999999.99m; // standard_cycle_time_sec DECIMAL(8,2)

    private readonly IProductRepository _productRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IProductRepository productRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<ProductService> logger)
    {
        _productRepository = productRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<ProductDto>> GetAllAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _productRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<ProductDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<ProductDto> GetByIdAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), productId);

        return MapToDto(product);
    }

    public async Task<ProductDto> CreateAsync(
        CreateProductRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(request, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(fields.Name, excludeProductId: null, cancellationToken);

        var product = new Product
        {
            // ProductId / ProductCode are never client-controlled: the code is issued by the repository from the PRODUCT
            // document sequence inside the insert's transaction.
            ProductName = fields.Name,
            Category = fields.Category,
            UnitOfMeasure = fields.UnitOfMeasure,
            StandardCycleTimeSec = fields.StandardCycleTimeSec,
            Remarks = fields.Remarks,
            IsActive = true, // a new product is always active - see CreateProductRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _productRepository.AddAsync(product, cancellationToken);

        await WriteAuditAsync(
            "ProductCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created product '{created.ProductName}' ({created.ProductCode}).",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<ProductDto> UpdateAsync(
        int productId, UpdateProductRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), productId);

        var fields = NormalizeAndValidate(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(fields.Name, productId, cancellationToken);

        // Product data carries nothing personal or secret, so old/new values are recorded for every changed field
        // (the Department convention).
        var details = new List<AuditLogDetailEntry>();
        void Track(string field, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) details.Add(new AuditLogDetailEntry(field, oldValue, newValue));
        }

        Track("product_name", product.ProductName, fields.Name);
        Track("category", product.Category, fields.Category);
        Track("unit_of_measure", product.UnitOfMeasure, fields.UnitOfMeasure);
        Track("standard_cycle_time_sec", Format(product.StandardCycleTimeSec), Format(fields.StandardCycleTimeSec));
        Track("remarks", product.Remarks, fields.Remarks);

        product.ProductName = fields.Name;
        product.Category = fields.Category;
        product.UnitOfMeasure = fields.UnitOfMeasure;
        product.StandardCycleTimeSec = fields.StandardCycleTimeSec;
        product.Remarks = fields.Remarks;
        product.UpdatedAt = _dateTimeProvider.UtcNow;
        product.UpdatedBy = actingUserId;

        var updated = await _productRepository.UpdateAsync(product, originalRowVersion!, cancellationToken);

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "ProductUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated product '{updated.ProductName}' ({updated.ProductCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<ProductDto> DeactivateAsync(
        int productId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException(nameof(Product), productId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (!product.IsActive)
        {
            throw new ConflictException("The product is already inactive.");
        }

        product.IsActive = false;
        product.UpdatedAt = _dateTimeProvider.UtcNow;
        product.UpdatedBy = actingUserId;

        var deactivated = await _productRepository.DeactivateAsync(product, cancellationToken);

        await WriteAuditAsync(
            "ProductDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated product '{deactivated.ProductName}' ({deactivated.ProductCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeProductId, CancellationToken cancellationToken)
    {
        if (await _productRepository.ExistsActiveByNameAsync(name, excludeProductId, cancellationToken))
        {
            throw new ConflictException($"A product named '{name}' already exists.");
        }
    }

    private static string? Format(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed record ProductFields(string Name, string? Category, string UnitOfMeasure, decimal? StandardCycleTimeSec, string? Remarks);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about what
    // a valid product looks like; the row version is only checked (and decoded) for Update.
    private static ProductFields NormalizeAndValidate(
        CreateProductRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var fields = new ProductFields(
            (request.ProductName ?? string.Empty).Trim(),
            Optional(request.Category),
            (request.UnitOfMeasure ?? string.Empty).Trim(),
            request.StandardCycleTimeSec,
            Optional(request.Remarks));

        var errors = new List<string>();

        if (fields.Name.Length == 0) errors.Add("ProductName is required.");
        else if (fields.Name.Length > NameMaxLength) errors.Add($"ProductName must be at most {NameMaxLength} characters.");

        if (fields.UnitOfMeasure.Length == 0) errors.Add("UnitOfMeasure is required.");
        else if (fields.UnitOfMeasure.Length > UnitOfMeasureMaxLength) errors.Add($"UnitOfMeasure must be at most {UnitOfMeasureMaxLength} characters.");

        if (fields.Category is not null && fields.Category.Length > CategoryMaxLength) errors.Add($"Category must be at most {CategoryMaxLength} characters.");
        if (fields.Remarks is not null && fields.Remarks.Length > RemarksMaxLength) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");

        if (fields.StandardCycleTimeSec is { } cycle)
        {
            if (cycle <= 0) errors.Add("StandardCycleTimeSec must be greater than 0.");
            else if (cycle > CycleTimeMax) errors.Add($"StandardCycleTimeSec must be at most {CycleTimeMax.ToString(CultureInfo.InvariantCulture)}.");
            else if (decimal.Round(cycle, 2) != cycle) errors.Add("StandardCycleTimeSec can have at most 2 decimal places.");
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

        return fields;
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
        string action, Product product, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = ProductModule,
            Action = action,
            EntityName = "Product",
            EntityId = product.ProductId,
            RecordRef = product.ProductCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static ProductDto MapToDto(Product product) => new()
    {
        ProductId = product.ProductId,
        ProductCode = product.ProductCode,
        ProductName = product.ProductName,
        Category = product.Category,
        UnitOfMeasure = product.UnitOfMeasure,
        StandardCycleTimeSec = product.StandardCycleTimeSec,
        Remarks = product.Remarks,
        IsActive = product.IsActive,
        CreatedAt = product.CreatedAt,
        CreatedBy = product.CreatedBy,
        UpdatedAt = product.UpdatedAt,
        UpdatedBy = product.UpdatedBy,
        RowVersion = Convert.ToBase64String(product.RowVersion),
    };
}
