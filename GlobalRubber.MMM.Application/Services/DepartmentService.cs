using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

public sealed class DepartmentService : IDepartmentService
{
    private const string DepartmentModule = "Department"; // audit "Module" value: the menu name, as audit_log.module documents
    private const int NameMaxLength = 100;                // masters.department_master.department_name NVARCHAR(100)
    private const int RemarksMaxLength = 500;             // remarks NVARCHAR(500)

    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(
        IDepartmentRepository departmentRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<DepartmentService> logger)
    {
        _departmentRepository = departmentRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<DepartmentDto>> GetAllAsync(DepartmentListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _departmentRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<DepartmentDto>.Create(
            items.Select(MapToDto).ToList(),
            query.PageNumber,
            query.PageSize,
            totalCount);
    }

    public async Task<DepartmentDto> GetByIdAsync(int departmentId, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), departmentId);

        return MapToDto(department);
    }

    public async Task<DepartmentDto> CreateAsync(
        CreateDepartmentRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var (name, remarks) = NormalizeAndValidate(request.DepartmentName, request.Remarks, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(name, excludeDepartmentId: null, cancellationToken);

        var department = new Department
        {
            // DepartmentId / DepartmentCode are never client-controlled: the code is issued by the repository
            // from the DEPARTMENT document sequence inside the insert's transaction.
            DepartmentName = name,
            Remarks = remarks,
            IsActive = true, // a new department is always active - see CreateDepartmentRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _departmentRepository.AddAsync(department, cancellationToken);

        await WriteAuditAsync(
            "DepartmentCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created department '{created.DepartmentName}' ({created.DepartmentCode}).",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<DepartmentDto> UpdateAsync(
        int departmentId, UpdateDepartmentRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), departmentId);

        var (name, remarks) = NormalizeAndValidate(request.DepartmentName, request.Remarks, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(name, departmentId, cancellationToken);

        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(department.DepartmentName, name, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("department_name", department.DepartmentName, name));
        }

        if (!string.Equals(department.Remarks, remarks, StringComparison.Ordinal))
        {
            details.Add(new AuditLogDetailEntry("remarks", department.Remarks, remarks));
        }

        department.DepartmentName = name;
        department.Remarks = remarks;
        department.UpdatedAt = _dateTimeProvider.UtcNow;
        department.UpdatedBy = actingUserId;

        var updated = await _departmentRepository.UpdateAsync(department, originalRowVersion!, cancellationToken);

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "DepartmentUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated department '{updated.DepartmentName}' ({updated.DepartmentCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<DepartmentDto> DeactivateAsync(
        int departmentId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), departmentId);

        // Explicit rather than a silent no-op: nothing is written and nothing is audited.
        if (!department.IsActive)
        {
            throw new ConflictException("The department is already inactive.");
        }

        department.IsActive = false;
        department.UpdatedAt = _dateTimeProvider.UtcNow;
        department.UpdatedBy = actingUserId;

        var deactivated = await _departmentRepository.DeactivateAsync(department, cancellationToken);

        await WriteAuditAsync(
            "DepartmentDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated department '{deactivated.DepartmentName}' ({deactivated.DepartmentCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    // Documented rule (System Analysis, validation table: "Name unique among active records", BE): another ACTIVE
    // department may not carry the same name. Inactive departments do not count.
    private async Task EnsureNameIsFreeAsync(string name, int? excludeDepartmentId, CancellationToken cancellationToken)
    {
        if (await _departmentRepository.ExistsActiveByNameAsync(name, excludeDepartmentId, cancellationToken))
        {
            throw new ConflictException($"A department named '{name}' already exists.");
        }
    }

    // Trim everything, blank remarks become null. One place so Create and Update can never disagree about what a
    // valid department looks like; the row version is only checked (and decoded) for Update.
    private static (string Name, string? Remarks) NormalizeAndValidate(
        string? rawName, string? rawRemarks, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var name = (rawName ?? string.Empty).Trim();
        var remarks = string.IsNullOrWhiteSpace(rawRemarks) ? null : rawRemarks.Trim();

        var errors = new List<string>();

        if (name.Length == 0) errors.Add("DepartmentName is required.");
        else if (name.Length > NameMaxLength) errors.Add($"DepartmentName must be at most {NameMaxLength} characters.");

        if (remarks is not null && remarks.Length > RemarksMaxLength) errors.Add($"Remarks must be at most {RemarksMaxLength} characters.");

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

        return (name, remarks);
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

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success
    // into a failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, Department department, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = DepartmentModule,
            Action = action,
            EntityName = "Department",
            EntityId = department.DepartmentId,
            RecordRef = department.DepartmentCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static DepartmentDto MapToDto(Department department) => new()
    {
        DepartmentId = department.DepartmentId,
        DepartmentCode = department.DepartmentCode,
        DepartmentName = department.DepartmentName,
        Remarks = department.Remarks,
        IsActive = department.IsActive,
        RowVersion = Convert.ToBase64String(department.RowVersion),
    };
}
