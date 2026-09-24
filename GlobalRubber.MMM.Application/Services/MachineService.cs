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
/// Machine master. Rules are exactly those the analysis (section 4.1, BR-04/BR-05, the validation table) and the database
/// define:
/// - name, machine type, department, location, maintenance frequency required (BR-04); criticality required from the
///   CK list (the form defaults it to Medium); column lengths from the table;
/// - maintenance frequency &gt; 0 (CK_machine_master_maintenance_frequency_days);
/// - department must exist and, when newly assigned, be active - an unchanged inactive one is kept;
/// - serial number unique whenever entered (UX_machine_master_serial_number);
/// - name unique among ACTIVE machines (the all-masters rule, applied as for the other masters - Q-27 is still open);
/// - a new machine is Running (BR-05); operational status and last/next maintenance dates are never taken from the
///   request (system-managed, Q-19). No future-date check on installation date (the analysis says there is none).
/// Responsible Engineer temporarily disabled. Database field and relationship intentionally retained for future re-enablement. It is neither accepted, validated, written, audited nor returned here; an existing
/// responsible_engineer_id is left exactly as it is.
/// </summary>
public sealed class MachineService : IMachineService
{
    private const string MachineModule = "Machine"; // audit "Module" value: the menu name, as for the other masters
    private const int NameMaxLength = 150;          // machine_name NVARCHAR(150)
    private const int TypeMaxLength = 100;          // machine_type NVARCHAR(100)
    private const int LocationMaxLength = 150;      // location NVARCHAR(150)
    private const int ManufacturerMaxLength = 100;  // manufacturer NVARCHAR(100)
    private const int ModelMaxLength = 100;         // model NVARCHAR(100)
    private const int SerialNumberMaxLength = 100;  // serial_number NVARCHAR(100)
    private const int CapacityMaxLength = 50;       // capacity NVARCHAR(50)
    private const int RemarksMaxLength = 500;       // remarks NVARCHAR(500)

    private readonly IMachineRepository _machineRepository;
    private readonly IDepartmentRepository _departmentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MachineService> _logger;

    public MachineService(
        IMachineRepository machineRepository,
        IDepartmentRepository departmentRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MachineService> logger)
    {
        _machineRepository = machineRepository;
        _departmentRepository = departmentRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MachineDto>> GetAllAsync(MachineListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _machineRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<MachineDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<MachineDto> GetByIdAsync(int machineId, CancellationToken cancellationToken)
    {
        var machine = await _machineRepository.GetByIdAsync(machineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), machineId);

        return MapToDto(machine);
    }

    public async Task<MachineDto> CreateAsync(
        CreateMachineRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(request, rowVersion: null, requireRowVersion: false, out _);

        var department = await LoadAssignableDepartmentAsync(fields.DepartmentId, cancellationToken);

        await EnsureUniqueAsync(fields, excludeMachineId: null, cancellationToken);

        var machine = new Machine
        {
            // MachineId / MachineCode are never client-controlled: the code is issued by the repository from the MACHINE
            // document sequence inside the insert's transaction.
            MachineName = fields.Name,
            MachineType = fields.MachineType,
            DepartmentId = department.DepartmentId,
            Location = fields.Location,
            Manufacturer = fields.Manufacturer,
            Model = fields.Model,
            SerialNumber = fields.SerialNumber,
            Capacity = fields.Capacity,
            InstallationDate = fields.InstallationDate,
            MaintenanceFrequencyDays = fields.MaintenanceFrequencyDays,
            Criticality = fields.Criticality,
            OperationalStatus = MachineOperationalStatus.Running, // BR-05
            Remarks = fields.Remarks,
            IsActive = true, // a new machine is always active - see CreateMachineRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _machineRepository.AddAsync(machine, cancellationToken);
        created.Department = department; // set after the save so EF never tries to insert the referenced row

        await WriteAuditAsync(
            "MachineCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created machine '{created.MachineName}' ({created.MachineCode}) in department {department.DepartmentCode}.",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<MachineDto> UpdateAsync(
        int machineId, UpdateMachineRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var machine = await _machineRepository.GetByIdAsync(machineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), machineId);

        var fields = NormalizeAndValidate(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        // Keeping the current department is always allowed (even if since deactivated - the form shows it flagged
        // instead of silently clearing it); choosing a different one requires an existing, active one.
        var oldDepartment = machine.Department;
        var department = fields.DepartmentId != machine.DepartmentId
            ? await LoadAssignableDepartmentAsync(fields.DepartmentId, cancellationToken)
            : oldDepartment;

        await EnsureUniqueAsync(fields, machineId, cancellationToken);

        // Machine data carries nothing personal or secret.
        var details = new List<AuditLogDetailEntry>();
        void Track(string field, string? oldValue, string? newValue)
        {
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal)) details.Add(new AuditLogDetailEntry(field, oldValue, newValue));
        }

        Track("machine_name", machine.MachineName, fields.Name);
        Track("machine_type", machine.MachineType, fields.MachineType);
        Track("department_code", oldDepartment?.DepartmentCode, department.DepartmentCode);
        Track("location", machine.Location, fields.Location);
        Track("manufacturer", machine.Manufacturer, fields.Manufacturer);
        Track("model", machine.Model, fields.Model);
        Track("serial_number", machine.SerialNumber, fields.SerialNumber);
        Track("capacity", machine.Capacity, fields.Capacity);
        Track("installation_date", FormatDate(machine.InstallationDate), FormatDate(fields.InstallationDate));
        Track("maintenance_frequency_days", machine.MaintenanceFrequencyDays.ToString(CultureInfo.InvariantCulture), fields.MaintenanceFrequencyDays.ToString(CultureInfo.InvariantCulture));
        Track("criticality", machine.Criticality, fields.Criticality);
        Track("remarks", machine.Remarks, fields.Remarks);

        machine.MachineName = fields.Name;
        machine.MachineType = fields.MachineType;
        machine.DepartmentId = department.DepartmentId;
        machine.Location = fields.Location;
        machine.Manufacturer = fields.Manufacturer;
        machine.Model = fields.Model;
        machine.SerialNumber = fields.SerialNumber;
        machine.Capacity = fields.Capacity;
        machine.InstallationDate = fields.InstallationDate;
        machine.MaintenanceFrequencyDays = fields.MaintenanceFrequencyDays;
        machine.Criticality = fields.Criticality;
        machine.Remarks = fields.Remarks;
        machine.UpdatedAt = _dateTimeProvider.UtcNow;
        machine.UpdatedBy = actingUserId;

        var updated = await _machineRepository.UpdateAsync(machine, originalRowVersion!, cancellationToken);
        updated.Department = department;

        var changeSummary = details.Count > 0
            ? $"Changed: {string.Join(", ", details.Select(d => d.FieldName))}."
            : "No field values changed.";

        await WriteAuditAsync(
            "MachineUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated machine '{updated.MachineName}' ({updated.MachineCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<MachineDto> DeactivateAsync(
        int machineId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var machine = await _machineRepository.GetByIdAsync(machineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), machineId);

        // Explicit rather than a silent no-op (same as the other masters): nothing is written or audited.
        if (!machine.IsActive)
        {
            throw new ConflictException("The machine is already inactive.");
        }

        machine.IsActive = false;
        machine.UpdatedAt = _dateTimeProvider.UtcNow;
        machine.UpdatedBy = actingUserId;

        var deactivated = await _machineRepository.DeactivateAsync(machine, cancellationToken);

        await WriteAuditAsync(
            "MachineDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated machine '{deactivated.MachineName}' ({deactivated.MachineCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    // The selected department must exist (404) and be active (400) - the same shape as Employee's department check.
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

    private async Task EnsureUniqueAsync(MachineFields fields, int? excludeMachineId, CancellationToken cancellationToken)
    {
        if (await _machineRepository.ExistsActiveByNameAsync(fields.Name, excludeMachineId, cancellationToken))
        {
            throw new ConflictException($"A machine named '{fields.Name}' already exists.");
        }

        if (fields.SerialNumber is not null
            && await _machineRepository.ExistsBySerialNumberAsync(fields.SerialNumber, excludeMachineId, cancellationToken))
        {
            throw new ConflictException($"A machine with serial number '{fields.SerialNumber}' already exists.");
        }
    }

    private static string? FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record MachineFields(
        string Name, string MachineType, int DepartmentId, string Location, string? Manufacturer, string? Model,
        string? SerialNumber, string? Capacity, DateOnly? InstallationDate, int MaintenanceFrequencyDays,
        string Criticality, string? Remarks);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about what
    // a valid machine looks like; the row version is only checked (and decoded) for Update.
    private static MachineFields NormalizeAndValidate(
        CreateMachineRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        static string Required(string? value) => (value ?? string.Empty).Trim();

        var name = Required(request.MachineName);
        var machineType = Required(request.MachineType);
        var location = Required(request.Location);
        var manufacturer = Optional(request.Manufacturer);
        var model = Optional(request.Model);
        var serialNumber = Optional(request.SerialNumber);
        var capacity = Optional(request.Capacity);
        var remarks = Optional(request.Remarks);
        var criticalityInput = Optional(request.Criticality);
        // Case-insensitive match onto the exact CK value, so "high" is stored as "High".
        var criticality = MachineCriticality.All.FirstOrDefault(c => string.Equals(c, criticalityInput, StringComparison.OrdinalIgnoreCase));

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

        RequiredText(name, NameMaxLength, "MachineName");
        RequiredText(machineType, TypeMaxLength, "MachineType");
        if (request.DepartmentId <= 0) errors.Add("DepartmentId is required.");
        RequiredText(location, LocationMaxLength, "Location");
        MaxLength(manufacturer, ManufacturerMaxLength, "Manufacturer");
        MaxLength(model, ModelMaxLength, "Model");
        MaxLength(serialNumber, SerialNumberMaxLength, "SerialNumber");
        MaxLength(capacity, CapacityMaxLength, "Capacity");
        MaxLength(remarks, RemarksMaxLength, "Remarks");

        if (request.MaintenanceFrequencyDays is null) errors.Add("MaintenanceFrequencyDays is required.");
        else if (request.MaintenanceFrequencyDays <= 0) errors.Add("MaintenanceFrequencyDays must be greater than 0.");

        if (criticalityInput is null) errors.Add("Criticality is required.");
        else if (criticality is null) errors.Add($"Criticality must be one of: {string.Join(", ", MachineCriticality.All)}.");

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

        return new MachineFields(
            name, machineType, request.DepartmentId, location, manufacturer, model, serialNumber, capacity,
            request.InstallationDate, request.MaintenanceFrequencyDays!.Value, criticality!, remarks);
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
        string action, Machine machine, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = MachineModule,
            Action = action,
            EntityName = "Machine",
            EntityId = machine.MachineId,
            RecordRef = machine.MachineCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static MachineDto MapToDto(Machine machine) => new()
    {
        MachineId = machine.MachineId,
        MachineCode = machine.MachineCode,
        MachineName = machine.MachineName,
        MachineType = machine.MachineType,
        DepartmentId = machine.DepartmentId,
        DepartmentName = machine.Department?.DepartmentName ?? string.Empty,
        DepartmentIsActive = machine.Department?.IsActive ?? false,
        Location = machine.Location,
        Manufacturer = machine.Manufacturer,
        Model = machine.Model,
        SerialNumber = machine.SerialNumber,
        Capacity = machine.Capacity,
        InstallationDate = machine.InstallationDate,
        MaintenanceFrequencyDays = machine.MaintenanceFrequencyDays,
        Criticality = machine.Criticality,
        OperationalStatus = machine.OperationalStatus,
        LastMaintenanceDate = machine.LastMaintenanceDate,
        NextMaintenanceDate = machine.NextMaintenanceDate,
        Remarks = machine.Remarks,
        IsActive = machine.IsActive,
        CreatedAt = machine.CreatedAt,
        CreatedBy = machine.CreatedBy,
        UpdatedAt = machine.UpdatedAt,
        UpdatedBy = machine.UpdatedBy,
        RowVersion = Convert.ToBase64String(machine.RowVersion),
    };
}
