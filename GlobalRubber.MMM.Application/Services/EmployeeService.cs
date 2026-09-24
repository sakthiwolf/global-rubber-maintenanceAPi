using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Employee master. Rules are exactly those the analysis (section 4.5) and the database define: name, designation
/// and department required; column lengths; the department must exist and - when newly assigned - be active.
/// Mobile/email format checks are only recommendations ([R]) in the analysis and are not enforced; employee-name
/// uniqueness is open question Q-27 and is not enforced either (two people may share a name).
/// </summary>
public sealed class EmployeeService : IEmployeeService
{
    private const string EmployeeModule = "Employee"; // audit "Module" value: the menu name, as for Department
    private const int NameMaxLength = 100;            // masters.employee_master.employee_name NVARCHAR(100)
    private const int DesignationMaxLength = 100;     // designation NVARCHAR(100)
    private const int MobileMaxLength = 15;           // mobile VARCHAR(15)
    private const int EmailMaxLength = 150;           // email VARCHAR(150)

    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        IEmployeeRepository employeeRepository,
        IDepartmentRepository departmentRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<EmployeeService> logger)
    {
        _employeeRepository = employeeRepository;
        _departmentRepository = departmentRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _employeeRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<EmployeeDto>.Create(
            items.Select(MapToDto).ToList(),
            query.PageNumber,
            query.PageSize,
            totalCount);
    }

    public async Task<EmployeeDto> GetByIdAsync(int employeeId, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        return MapToDto(employee);
    }

    public async Task<EmployeeDto> CreateAsync(
        CreateEmployeeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(
            request.EmployeeName, request.Designation, request.DepartmentId, request.Mobile, request.Email,
            rowVersion: null, requireRowVersion: false, out _);

        var department = await LoadAssignableDepartmentAsync(fields.DepartmentId, cancellationToken);

        var employee = new Employee
        {
            // EmployeeId / EmployeeCode are never client-controlled: the code is issued by the repository from the
            // EMPLOYEE document sequence inside the insert's transaction.
            EmployeeName = fields.Name,
            Designation = fields.Designation,
            DepartmentId = department.DepartmentId,
            Mobile = fields.Mobile,
            Email = fields.Email,
            IsActive = true, // a new employee is always active - see CreateEmployeeRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _employeeRepository.AddAsync(employee, cancellationToken);
        created.Department = department; // set after the save so EF never tries to insert the department

        await WriteAuditAsync(
            "EmployeeCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created employee '{created.EmployeeName}' ({created.EmployeeCode}) in department {department.DepartmentCode}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<EmployeeDto> UpdateAsync(
        int employeeId, UpdateEmployeeRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        var fields = NormalizeAndValidate(
            request.EmployeeName, request.Designation, request.DepartmentId, request.Mobile, request.Email,
            request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        // Keeping the current department is always allowed (even if it has since been deactivated - the form shows
        // it flagged instead of silently clearing it); choosing a different one requires an existing, active one.
        var departmentChanged = fields.DepartmentId != employee.DepartmentId;
        var oldDepartment = employee.Department;
        var department = departmentChanged
            ? await LoadAssignableDepartmentAsync(fields.DepartmentId, cancellationToken)
            : oldDepartment;

        // Changed field names in the description. Old/new values only for the non-contact fields - mobile and
        // email are personal data and, as in UserService, are not audit material.
        var changes = new List<string>();
        var details = new List<AuditLogDetailEntry>();
        if (!string.Equals(employee.EmployeeName, fields.Name, StringComparison.Ordinal))
        {
            changes.Add("employee_name");
            details.Add(new AuditLogDetailEntry("employee_name", employee.EmployeeName, fields.Name));
        }

        if (!string.Equals(employee.Designation, fields.Designation, StringComparison.Ordinal))
        {
            changes.Add("designation");
            details.Add(new AuditLogDetailEntry("designation", employee.Designation, fields.Designation));
        }

        if (departmentChanged)
        {
            changes.Add("department");
            details.Add(new AuditLogDetailEntry("department_code", oldDepartment?.DepartmentCode, department.DepartmentCode));
        }

        if (!string.Equals(employee.Mobile, fields.Mobile, StringComparison.Ordinal)) changes.Add("mobile");
        if (!string.Equals(employee.Email, fields.Email, StringComparison.Ordinal)) changes.Add("email");

        employee.EmployeeName = fields.Name;
        employee.Designation = fields.Designation;
        employee.DepartmentId = department.DepartmentId;
        employee.Mobile = fields.Mobile;
        employee.Email = fields.Email;
        employee.UpdatedAt = _dateTimeProvider.UtcNow;
        employee.UpdatedBy = actingUserId;

        var updated = await _employeeRepository.UpdateAsync(employee, originalRowVersion!, cancellationToken);
        updated.Department = department;

        var changeSummary = changes.Count > 0 ? $"Changed: {string.Join(", ", changes)}." : "No field values changed.";

        await WriteAuditAsync(
            "EmployeeUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated employee '{updated.EmployeeName}' ({updated.EmployeeCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<EmployeeDto> DeactivateAsync(
        int employeeId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var employee = await _employeeRepository.GetByIdAsync(employeeId, cancellationToken)
            ?? throw new NotFoundException(nameof(Employee), employeeId);

        // Explicit rather than a silent no-op (same as Role/Department): nothing is written and nothing is audited.
        if (!employee.IsActive)
        {
            throw new ConflictException("The employee is already inactive.");
        }

        employee.IsActive = false;
        employee.UpdatedAt = _dateTimeProvider.UtcNow;
        employee.UpdatedBy = actingUserId;

        var deactivated = await _employeeRepository.DeactivateAsync(employee, cancellationToken);

        await WriteAuditAsync(
            "EmployeeDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated employee '{deactivated.EmployeeName}' ({deactivated.EmployeeCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    // The selected department must exist (404) and be active (400) - the same shape as UserService's role check.
    private async Task<Department> LoadAssignableDepartmentAsync(int departmentId, CancellationToken cancellationToken)
    {
        var department = await _departmentRepository.GetByIdAsync(departmentId, cancellationToken)
            ?? throw new NotFoundException(nameof(Department), departmentId);

        if (!department.IsActive)
        {
            throw new ValidationException("The selected department is not active.");
        }

        return department;
    }

    private sealed record EmployeeFields(string Name, string Designation, int DepartmentId, string? Mobile, string? Email);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about
    // what a valid employee looks like; the row version is only checked (and decoded) for Update.
    private static EmployeeFields NormalizeAndValidate(
        string? rawName, string? rawDesignation, int departmentId, string? rawMobile, string? rawEmail,
        string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        var name = (rawName ?? string.Empty).Trim();
        var designation = (rawDesignation ?? string.Empty).Trim();
        var mobile = string.IsNullOrWhiteSpace(rawMobile) ? null : rawMobile.Trim();
        var email = string.IsNullOrWhiteSpace(rawEmail) ? null : rawEmail.Trim();

        var errors = new List<string>();

        if (name.Length == 0) errors.Add("EmployeeName is required.");
        else if (name.Length > NameMaxLength) errors.Add($"EmployeeName must be at most {NameMaxLength} characters.");

        if (designation.Length == 0) errors.Add("Designation is required.");
        else if (designation.Length > DesignationMaxLength) errors.Add($"Designation must be at most {DesignationMaxLength} characters.");

        if (departmentId <= 0) errors.Add("DepartmentId is required.");

        if (mobile is not null && mobile.Length > MobileMaxLength) errors.Add($"Mobile must be at most {MobileMaxLength} characters.");
        if (email is not null && email.Length > EmailMaxLength) errors.Add($"Email must be at most {EmailMaxLength} characters.");

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

        return new EmployeeFields(name, designation, departmentId, mobile, email);
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
        string action, Employee employee, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = EmployeeModule,
            Action = action,
            EntityName = "Employee",
            EntityId = employee.EmployeeId,
            RecordRef = employee.EmployeeCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static EmployeeDto MapToDto(Employee employee) => new()
    {
        EmployeeId = employee.EmployeeId,
        EmployeeCode = employee.EmployeeCode,
        EmployeeName = employee.EmployeeName,
        Designation = employee.Designation,
        DepartmentId = employee.DepartmentId,
        DepartmentName = employee.Department?.DepartmentName ?? string.Empty,
        DepartmentIsActive = employee.Department?.IsActive ?? false,
        Mobile = employee.Mobile,
        Email = employee.Email,
        IsActive = employee.IsActive,
        CreatedAt = employee.CreatedAt,
        CreatedBy = employee.CreatedBy,
        UpdatedAt = employee.UpdatedAt,
        UpdatedBy = employee.UpdatedBy,
        RowVersion = Convert.ToBase64String(employee.RowVersion),
    };
}
