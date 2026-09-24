using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Maintenance Type master. Rules are exactly those the analysis (section 4.8 and the validation table) and the database
/// define: name required (NVARCHAR(100)); applies-to required, one of Machine / Mold / Both
/// (CK_maintenance_type_master_applies_to); name unique among ACTIVE maintenance types (the all-masters rule, applied as
/// for the other masters - Q-27 is still open).
/// </summary>
public sealed class MaintenanceTypeService : IMaintenanceTypeService
{
    private const string MaintenanceTypeModule = "Maintenance Type"; // audit "Module" value: the menu name
    private const int NameMaxLength = 100;                            // maintenance_type_name NVARCHAR(100)

    private readonly IMaintenanceTypeRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MaintenanceTypeService> _logger;

    public MaintenanceTypeService(
        IMaintenanceTypeRepository repository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MaintenanceTypeService> logger)
    {
        _repository = repository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MaintenanceTypeDto>> GetAllAsync(MaintenanceTypeListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, cancellationToken);

        return PagedResult<MaintenanceTypeDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<MaintenanceTypeDto> GetByIdAsync(int maintenanceTypeId, CancellationToken cancellationToken)
    {
        var maintenanceType = await _repository.GetByIdAsync(maintenanceTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceType), maintenanceTypeId);

        return MapToDto(maintenanceType);
    }

    public async Task<MaintenanceTypeDto> CreateAsync(
        CreateMaintenanceTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var (name, appliesTo) = NormalizeAndValidate(request, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var maintenanceType = new MaintenanceType
        {
            // The id / code are never client-controlled: the code is issued by the repository from the MAINTENANCE_TYPE
            // document sequence inside the insert's transaction.
            MaintenanceTypeName = name,
            AppliesTo = appliesTo,
            IsActive = true, // a new maintenance type is always active - see CreateMaintenanceTypeRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _repository.AddAsync(maintenanceType, cancellationToken);

        await WriteAuditAsync(
            "MaintenanceTypeCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created maintenance type '{created.MaintenanceTypeName}' ({created.MaintenanceTypeCode}) applying to {created.AppliesTo}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<MaintenanceTypeDto> UpdateAsync(
        int maintenanceTypeId, UpdateMaintenanceTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var maintenanceType = await _repository.GetByIdAsync(maintenanceTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceType), maintenanceTypeId);

        var (name, appliesTo) = NormalizeAndValidate(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(name, maintenanceTypeId, cancellationToken);

        // Nothing personal or secret: old/new values are recorded for every changed field (the Department convention).
        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(maintenanceType.MaintenanceTypeName, name, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("maintenance_type_name", maintenanceType.MaintenanceTypeName, name));
        }

        if (!string.Equals(maintenanceType.AppliesTo, appliesTo, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("applies_to", maintenanceType.AppliesTo, appliesTo));
        }

        maintenanceType.MaintenanceTypeName = name;
        maintenanceType.AppliesTo = appliesTo;
        maintenanceType.UpdatedAt = _dateTimeProvider.UtcNow;
        maintenanceType.UpdatedBy = actingUserId;

        var updated = await _repository.UpdateAsync(maintenanceType, originalRowVersion!, cancellationToken);

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "MaintenanceTypeUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated maintenance type '{updated.MaintenanceTypeName}' ({updated.MaintenanceTypeCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<MaintenanceTypeDto> DeactivateAsync(
        int maintenanceTypeId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var maintenanceType = await _repository.GetByIdAsync(maintenanceTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(MaintenanceType), maintenanceTypeId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (!maintenanceType.IsActive)
        {
            throw new ConflictException("The maintenance type is already inactive.");
        }

        maintenanceType.IsActive = false;
        maintenanceType.UpdatedAt = _dateTimeProvider.UtcNow;
        maintenanceType.UpdatedBy = actingUserId;

        var deactivated = await _repository.DeactivateAsync(maintenanceType, cancellationToken);

        await WriteAuditAsync(
            "MaintenanceTypeDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated maintenance type '{deactivated.MaintenanceTypeName}' ({deactivated.MaintenanceTypeCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        if (await _repository.ExistsActiveByNameAsync(name, excludeId, cancellationToken))
        {
            throw new ConflictException($"A maintenance type named '{name}' already exists.");
        }
    }

    // Trim everything; applies-to is matched case-insensitively onto the exact CK value. One place so Create and Update
    // can never disagree about what a valid maintenance type looks like; the row version is only checked for Update.
    private static (string Name, string AppliesTo) NormalizeAndValidate(
        CreateMaintenanceTypeRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var name = (request.MaintenanceTypeName ?? string.Empty).Trim();
        var appliesToInput = string.IsNullOrWhiteSpace(request.AppliesTo) ? null : request.AppliesTo.Trim();
        var appliesTo = MaintenanceTypeAppliesTo.All.FirstOrDefault(a => string.Equals(a, appliesToInput, StringComparison.OrdinalIgnoreCase));

        var errors = new List<string>();

        if (name.Length == 0) errors.Add("MaintenanceTypeName is required.");
        else if (name.Length > NameMaxLength) errors.Add($"MaintenanceTypeName must be at most {NameMaxLength} characters.");

        if (appliesToInput is null) errors.Add("AppliesTo is required.");
        else if (appliesTo is null) errors.Add($"AppliesTo must be one of: {string.Join(", ", MaintenanceTypeAppliesTo.All)}.");

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

        return (name, appliesTo!);
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
        string action, MaintenanceType maintenanceType, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MaintenanceTypeModule,
            Action = action,
            EntityName = "MaintenanceType",
            EntityId = maintenanceType.MaintenanceTypeId,
            RecordRef = maintenanceType.MaintenanceTypeCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static MaintenanceTypeDto MapToDto(MaintenanceType maintenanceType) => new()
    {
        MaintenanceTypeId = maintenanceType.MaintenanceTypeId,
        MaintenanceTypeCode = maintenanceType.MaintenanceTypeCode,
        MaintenanceTypeName = maintenanceType.MaintenanceTypeName,
        AppliesTo = maintenanceType.AppliesTo,
        IsActive = maintenanceType.IsActive,
        CreatedAt = maintenanceType.CreatedAt,
        CreatedBy = maintenanceType.CreatedBy,
        UpdatedAt = maintenanceType.UpdatedAt,
        UpdatedBy = maintenanceType.UpdatedBy,
        RowVersion = Convert.ToBase64String(maintenanceType.RowVersion),
    };
}
