using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Breakdown Type master. Rules: name required (NVARCHAR(100)), name unique among ACTIVE breakdown types
/// (the all-masters rule - Q-27), code is issued from BREAKDOWN_TYPE sequence.
/// </summary>
public sealed class BreakdownTypeService : IBreakdownTypeService
{
    private const string BreakdownTypeModule = "Breakdown Type";
    private const int NameMaxLength = 100;

    private readonly IBreakdownTypeRepository _repository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<BreakdownTypeService> _logger;

    public BreakdownTypeService(
        IBreakdownTypeRepository repository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<BreakdownTypeService> logger)
    {
        _repository = repository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<BreakdownTypeDto>> GetAllAsync(
        BreakdownTypeListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _repository.GetAllAsync(query, cancellationToken);
        return PagedResult<BreakdownTypeDto>.Create(
            items.Select(MapToDto).ToList(),
            query.PageNumber,
            query.PageSize,
            totalCount);
    }

    public async Task<BreakdownTypeDto> GetByIdAsync(
        int breakdownTypeId, CancellationToken cancellationToken)
    {
        var breakdownType = await _repository.GetByIdAsync(breakdownTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(BreakdownType), breakdownTypeId);

        return MapToDto(breakdownType);
    }

    public async Task<BreakdownTypeDto> CreateAsync(
        CreateBreakdownTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var name = NormalizeAndValidate(request);

        await EnsureNameIsFreeAsync(name, excludeId: null, cancellationToken);

        var breakdownType = new BreakdownType
        {
            BreakdownTypeName = name,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _repository.AddAsync(breakdownType, cancellationToken);

        await WriteAuditAsync(
            "BreakdownTypeCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created breakdown type '{created.BreakdownTypeName}' ({created.BreakdownTypeCode}).",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<BreakdownTypeDto> UpdateAsync(
        int breakdownTypeId, UpdateBreakdownTypeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var breakdownType = await _repository.GetByIdAsync(breakdownTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(BreakdownType), breakdownTypeId);

        var (name, originalRowVersion) = NormalizeAndValidate(request);

        await EnsureNameIsFreeAsync(name, breakdownTypeId, cancellationToken);

        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(breakdownType.BreakdownTypeName, name, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("breakdown_type_name", breakdownType.BreakdownTypeName, name));
        }

        breakdownType.BreakdownTypeName = name;
        breakdownType.UpdatedAt = _dateTimeProvider.UtcNow;
        breakdownType.UpdatedBy = actingUserId;

        var updated = await _repository.UpdateAsync(breakdownType, originalRowVersion, cancellationToken);

        await WriteAuditAsync(
            "BreakdownTypeUpdated", updated, actingUserId, ipAddress,
            actor => details.Count == 0
                ? $"{actor} updated breakdown type '{updated.BreakdownTypeName}' with no effective change."
                : $"{actor} updated breakdown type '{updated.BreakdownTypeName}'.",
            details: details.Count == 0 ? null : details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<BreakdownTypeDto> DeactivateAsync(
        int breakdownTypeId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var breakdownType = await _repository.GetByIdAsync(breakdownTypeId, cancellationToken)
            ?? throw new NotFoundException(nameof(BreakdownType), breakdownTypeId);

        if (!breakdownType.IsActive)
            throw new ConflictException("The breakdown type is already inactive.");

        breakdownType.IsActive = false;
        breakdownType.UpdatedAt = _dateTimeProvider.UtcNow;
        breakdownType.UpdatedBy = actingUserId;

        var deactivated = await _repository.DeactivateAsync(breakdownType, cancellationToken);

        await WriteAuditAsync(
            "BreakdownTypeDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated breakdown type '{deactivated.BreakdownTypeName}'.",
            details: null, cancellationToken);

        return MapToDto(deactivated);
    }

    private string NormalizeAndValidate(CreateBreakdownTypeRequest request)
    {
        var errors = new List<string>();
        var name = request.BreakdownTypeName?.Trim();

        if (string.IsNullOrEmpty(name))
            errors.Add("Name is required.");

        if (name?.Length > NameMaxLength)
            errors.Add($"Name cannot exceed {NameMaxLength} characters.");

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return name!;
    }

    private (string Name, byte[] OriginalRowVersion) NormalizeAndValidate(UpdateBreakdownTypeRequest request)
    {
        var errors = new List<string>();
        var name = request.BreakdownTypeName?.Trim();

        if (string.IsNullOrEmpty(name))
            errors.Add("Name is required.");

        if (name?.Length > NameMaxLength)
            errors.Add($"Name cannot exceed {NameMaxLength} characters.");

        byte[] originalRowVersion;
        if (!TryDecodeRowVersion(request.RowVersion, out originalRowVersion))
            errors.Add("RowVersion is not valid.");

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return (name!, originalRowVersion);
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeId, CancellationToken cancellationToken)
    {
        var exists = await _repository.ExistsActiveByNameAsync(name, excludeId, cancellationToken);
        if (exists)
            throw new ConflictException("A breakdown type with the same name already exists.");
    }

    private async Task WriteAuditAsync(
        string action, BreakdownType entity, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details,
        CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = BreakdownTypeModule,
            Action = action,
            EntityName = "BreakdownType",
            EntityId = entity.BreakdownTypeId,
            RecordRef = entity.BreakdownTypeCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static bool TryDecodeRowVersion(string? rowVersionBase64, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrEmpty(rowVersionBase64)) return false;

        try
        {
            rowVersion = Convert.FromBase64String(rowVersionBase64);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static BreakdownTypeDto MapToDto(BreakdownType entity) =>
        new BreakdownTypeDto
        {
            BreakdownTypeId = entity.BreakdownTypeId,
            BreakdownTypeCode = entity.BreakdownTypeCode,
            BreakdownTypeName = entity.BreakdownTypeName,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy,
            RowVersion = Convert.ToBase64String(entity.RowVersion),
        };
}