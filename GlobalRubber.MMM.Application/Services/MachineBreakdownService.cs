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
/// Machine Breakdown service - report, track and resolve breakdown incidents.
///
/// Business rules:
///  - Machine must exist and be active.
///  - BreakdownDate, BreakdownTime, MachineId and Problem are required.
///  - ReportedBy is free text (max 100 chars); NOT validated against Employee master.
///  - Priority must be Low / Medium / High / Critical; defaults to Medium when omitted.
///  - BreakdownTypeId, if supplied, must reference an existing (404) and active (400) breakdown type.
///  - Initial Stage = Reported.
///  - Stage advances forward only: Reported → Assigned → Maintenance Started → Resolved → Closed. A Closed breakdown
///    cannot change any more (409, like any "already final" record in this project).
///  - Assigned: the engineer stays optional (analysis D-12); a given one must exist (404) and be active (400).
///  - Root Cause / Corrective Action: optional, at most 1000 characters each (NVARCHAR(1000)).
///  - List paging: PageNumber >= 1 and 1 <= PageSize <= 100 (PaginationDefaults.MaxPageSize), else 400.
///  - BR-16: reporting sets the machine's operational status to Breakdown. BR-20: resolving sets it back to Running when
///    no other breakdown of the machine is unresolved and the machine is still in Breakdown (MachineBreakdownRules). The
///    machine change is written in the same transaction and listed in the breakdown's own audit entry
///    (machine_operational_status (code)), as Machine PM records its machine changes. The last maintenance date is left
///    alone - whether a breakdown should update it is open question Q-20.
///  - Assigned To (migration 021): an existing active employee (AssignedEngineerId) OR a typed name (AssignedToName,
///    max 100) - never both; a typed name never creates an employee.
///  - Reopen: only a Closed breakdown, back to Resolved (its previous stage); ClosedAt is cleared, everything else (number,
///    timestamps, downtime, root cause) is kept. Approve permission (controller). Audited as MachineBreakdownReopened.
///  - Notifications (migration 021) for every workflow event, through INotificationPublisher after the write has been
///    committed, to module TRN_MACHINE_BREAKDOWN (delivered to the users whose role can View it). A notification that
///    cannot be written never fails the breakdown operation.
/// </summary>
public sealed class MachineBreakdownService : IMachineBreakdownService
{
    private const string BreakdownModule = "Machine Breakdown";
    private const int ProblemMaxLength = 500;
    private const int ReportedByMaxLength = 100;
    private const int DescriptionMaxLength = 1000;
    private const int RootCauseMaxLength = 1000;        // root_cause NVARCHAR(1000)
    private const int CorrectiveActionMaxLength = 1000; // corrective_action NVARCHAR(1000)
    private const int AssignedToNameMaxLength = 100;    // assigned_to_name NVARCHAR(100)
    private const string BreakdownPagePath = "/transactions/machine-breakdown";

    private readonly IMachineBreakdownRepository _repository;
    private readonly IMachineRepository _machineRepository;
    private readonly IBreakdownTypeRepository _breakdownTypeRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<MachineBreakdownService> _logger;

    public MachineBreakdownService(
        IMachineBreakdownRepository repository,
        IMachineRepository machineRepository,
        IBreakdownTypeRepository breakdownTypeRepository,
        IEmployeeRepository employeeRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        INotificationPublisher notificationPublisher,
        ILogger<MachineBreakdownService> logger)
    {
        _repository = repository;
        _machineRepository = machineRepository;
        _breakdownTypeRepository = breakdownTypeRepository;
        _employeeRepository = employeeRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    public async Task<PagedResult<MachineBreakdownDto>> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken)
    {
        ValidatePaging(query);

        var (items, totalCount) = await _repository.GetAllAsync(query, cancellationToken);

        return PagedResult<MachineBreakdownDto>.Create(
            items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<MachineBreakdownDto> GetByIdAsync(int machineBreakdownId, CancellationToken cancellationToken)
    {
        var breakdown = await _repository.GetByIdAsync(machineBreakdownId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachineBreakdown), machineBreakdownId);

        return MapToDto(breakdown);
    }

    public async Task<MachineBreakdownDto> CreateAsync(
        CreateMachineBreakdownRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var input = NormalizeAndValidateCreate(request);

        // Machine must exist and be active.
        var machine = await _machineRepository.GetByIdAsync(input.MachineId, cancellationToken)
            ?? throw new NotFoundException(nameof(Machine), input.MachineId);

        if (!machine.IsActive)
        {
            throw new ValidationException("The selected machine is not active.");
        }

        // Breakdown type is optional; when chosen it must exist and be active.
        if (input.BreakdownTypeId is { } breakdownTypeId)
        {
            var breakdownType = await _breakdownTypeRepository.GetByIdAsync(breakdownTypeId, cancellationToken)
                ?? throw new NotFoundException(nameof(BreakdownType), breakdownTypeId);

            if (!breakdownType.IsActive)
            {
                throw new ValidationException("The selected breakdown type is not active.");
            }
        }

        var now = _dateTimeProvider.UtcNow;
        var breakdown = new MachineBreakdown
        {
            MachineId = input.MachineId,
            BreakdownDate = input.BreakdownDate,
            BreakdownTime = input.BreakdownTime,
            ReportedBy = input.ReportedBy,
            Problem = input.Problem,
            BreakdownTypeId = input.BreakdownTypeId,
            Priority = input.Priority,
            Description = input.Description,
            Stage = BreakdownStage.Reported,
            CreatedAt = now,
            CreatedBy = actingUserId,
        };

        // BR-16: the machine goes into Breakdown, in the same transaction (filled in by the plan, read after the commit).
        var machineStatus = new MachineStatusChange();
        var plan = new BreakdownMachineStatusPlan
        {
            ApplyToLockedMachine = (lockedMachine, _) =>
                ApplyMachineStatus(lockedMachine, MachineBreakdownRules.OperationalStatusAfterReport(lockedMachine.OperationalStatus), now, actingUserId, machineStatus),
        };

        var created = await _repository.AddAsync(breakdown, plan, cancellationToken);
        created.Machine = machine;

        await WriteAuditAsync(
            BreakdownAuditNames.Created, created, actingUserId, ipAddress,
            actor => $"{actor} reported breakdown {created.BreakdownNo} for machine {machine.MachineCode}. Problem: {input.Problem}" + machineStatus.Summary(machine.MachineCode),
            cancellationToken, machineStatus.Details(machine.MachineCode));

        await NotifyAsync(created, NotificationTypes.BreakdownReported, $"BreakdownReported:{created.MachineBreakdownId}",
            created.Priority switch { BreakdownPriority.Critical => NotificationSeverity.Critical, BreakdownPriority.High => NotificationSeverity.Warning, _ => NotificationSeverity.Info },
            "New breakdown reported",
            $"{created.BreakdownNo} reported for {machine.MachineCode} - {machine.MachineName}. Priority: {created.Priority}.",
            actingUserId, cancellationToken);

        return MapToDto(created);
    }

    public async Task<MachineBreakdownDto> AdvanceStageAsync(
        int machineBreakdownId, AdvanceMachineBreakdownStageRequest request,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(machineBreakdownId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachineBreakdown), machineBreakdownId);

        var (newStage, originalRowVersion) = ValidateStageAdvance(existing, request);

        // Assigned: the engineer is optional (D-12); a given one must exist and be active - it is always newly chosen
        // here, because a breakdown is assigned only once.
        Employee? engineer = null;
        var assignedToName = string.IsNullOrWhiteSpace(request.AssignedToName) ? null : request.AssignedToName.Trim();
        if (newStage == BreakdownStage.Assigned && request.AssignedEngineerId is { } engineerId)
        {
            engineer = await _employeeRepository.GetByIdAsync(engineerId, cancellationToken)
                ?? throw new NotFoundException(nameof(Employee), engineerId);

            if (!engineer.IsActive)
            {
                throw new ValidationException("The selected engineer is not active.");
            }
        }

        var now = _dateTimeProvider.UtcNow;
        var oldStage = existing.Stage;

        existing.Stage = newStage;
        existing.UpdatedAt = now;
        existing.UpdatedBy = actingUserId;

        switch (newStage)
        {
            case BreakdownStage.Assigned:
                existing.AssignedEngineerId = engineer?.EmployeeId;
                existing.AssignedToName = engineer is null ? assignedToName : null; // the two are never both set
                existing.AssignedAt = now;
                break;

            case BreakdownStage.MaintenanceStarted:
                existing.MaintenanceStartedAt = now;
                break;

            case BreakdownStage.Resolved:
                existing.ResolvedAt = now;
                existing.RootCause = string.IsNullOrWhiteSpace(request.RootCause) ? null : request.RootCause.Trim();
                existing.CorrectiveAction = string.IsNullOrWhiteSpace(request.CorrectiveAction) ? null : request.CorrectiveAction.Trim();
                if (existing.MaintenanceStartedAt.HasValue)
                {
                    var hrs = (now - existing.MaintenanceStartedAt.Value).TotalHours;
                    existing.DowntimeHours = Math.Max(0, Math.Round((decimal)hrs, 1));
                }
                break;

            case BreakdownStage.Closed:
                existing.ClosedAt = now;
                break;
        }

        // BR-20: resolving returns the machine to Running (MachineBreakdownRules), in the same transaction.
        var machineStatus = new MachineStatusChange();
        var plan = newStage == BreakdownStage.Resolved
            ? new BreakdownMachineStatusPlan
            {
                ApplyToLockedMachine = (lockedMachine, otherUnresolved) =>
                    ApplyMachineStatus(lockedMachine, MachineBreakdownRules.OperationalStatusAfterResolve(lockedMachine.OperationalStatus, otherUnresolved), now, actingUserId, machineStatus),
            }
            : null;

        var updated = await _repository.UpdateStageAsync(existing, originalRowVersion, plan, cancellationToken);
        if (engineer is not null)
        {
            updated.AssignedEngineer = engineer; // for the response's engineer name
        }

        var machineCode = updated.Machine?.MachineCode ?? string.Empty;
        await WriteAuditAsync(
            BreakdownAuditNames.StageChanged, updated, actingUserId, ipAddress,
            actor => $"{actor} moved breakdown {updated.BreakdownNo} from \"{oldStage}\" to \"{newStage}\"." + machineStatus.Summary(machineCode),
            cancellationToken, machineStatus.Details(machineCode));

        var (type, title, verb) = newStage switch
        {
            BreakdownStage.Assigned => (NotificationTypes.BreakdownAssigned, "Breakdown assigned", "has been assigned"),
            BreakdownStage.MaintenanceStarted => (NotificationTypes.BreakdownStarted, "Breakdown maintenance started", "is under maintenance"),
            BreakdownStage.Resolved => (NotificationTypes.BreakdownResolved, "Breakdown resolved", "has been resolved"),
            _ => (NotificationTypes.BreakdownClosed, "Breakdown closed", "has been closed"),
        };
        await NotifyAsync(updated, type, $"{type}:{updated.MachineBreakdownId}:{EventStamp(now)}", NotificationSeverity.Info, title,
            $"{updated.BreakdownNo} for {machineCode} {verb}.", actingUserId, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<MachineBreakdownDto> ReopenAsync(
        int machineBreakdownId, ReopenMachineBreakdownRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(machineBreakdownId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachineBreakdown), machineBreakdownId);

        if (existing.Stage != BreakdownStage.Closed)
        {
            throw new ConflictException("Only a closed breakdown can be reopened.");
        }

        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            throw new ValidationException("RowVersion is required to update a breakdown.");
        }

        byte[] originalRowVersion;
        try
        {
            originalRowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch
        {
            throw new ValidationException("RowVersion is not a valid base-64 string.");
        }

        // Back to the stage before Closed. Everything recorded so far (number, timestamps, downtime, root cause) is kept;
        // only the closure is undone. The machine is not touched: a Resolved breakdown no longer keeps it down.
        var now = _dateTimeProvider.UtcNow;
        var closedAt = existing.ClosedAt;
        existing.Stage = BreakdownStage.Resolved;
        existing.ClosedAt = null;
        existing.UpdatedAt = now;
        existing.UpdatedBy = actingUserId;

        var updated = await _repository.UpdateStageAsync(existing, originalRowVersion, machinePlan: null, cancellationToken);
        var machineCode = updated.Machine?.MachineCode ?? string.Empty;

        await WriteAuditAsync(
            BreakdownAuditNames.Reopened, updated, actingUserId, ipAddress,
            actor => $"{actor} reopened breakdown {updated.BreakdownNo} (\"{BreakdownStage.Closed}\" -> \"{BreakdownStage.Resolved}\").",
            cancellationToken,
            new[]
            {
                new AuditLogDetailEntry("stage", BreakdownStage.Closed, BreakdownStage.Resolved),
                new AuditLogDetailEntry("closed_at", closedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), null),
            });

        await NotifyAsync(updated, NotificationTypes.BreakdownReopened, $"BreakdownReopened:{updated.MachineBreakdownId}:{EventStamp(now)}",
            NotificationSeverity.Warning, "Breakdown reopened", $"{updated.BreakdownNo} for {machineCode} has been reopened.", actingUserId, cancellationToken);

        return MapToDto(updated);
    }

    // ============================================================ Notifications

    private static string EventStamp(DateTime utc) => utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    // After the business write has committed. Only what the breakdown list itself shows (number, machine, stage,
    // priority) - never the problem text, the engineer or the reporter. Delivered to the users whose role can View
    // TRN_MACHINE_BREAKDOWN; the publisher never throws into the caller.
    private Task NotifyAsync(
        MachineBreakdown breakdown, string type, string eventKey, string severity, string title, string message,
        int? actingUserId, CancellationToken cancellationToken) =>
        _notificationPublisher.PublishAsync(new NewNotification(
            type, ModuleCodes.TrnMachineBreakdown, severity, title, message, eventKey,
            EntityName: nameof(MachineBreakdown), EntityId: breakdown.MachineBreakdownId, RecordRef: breakdown.BreakdownNo,
            LinkPath: $"{BreakdownPagePath}?open={breakdown.MachineBreakdownId}", CreatedBy: actingUserId), cancellationToken);

    // ============================================================ Validation

    private sealed record ValidCreateInput(
        int MachineId, DateOnly BreakdownDate, TimeOnly BreakdownTime,
        string? ReportedBy, string Problem, int? BreakdownTypeId, string Priority, string? Description);

    private static ValidCreateInput NormalizeAndValidateCreate(CreateMachineBreakdownRequest request)
    {
        var errors = new List<string>();

        if (request.MachineId is null or <= 0)
            errors.Add("MachineId is required.");

        if (request.BreakdownDate is null)
            errors.Add("BreakdownDate is required.");

        if (request.BreakdownTime is null)
            errors.Add("BreakdownTime is required.");

        var problem = string.IsNullOrWhiteSpace(request.Problem) ? null : request.Problem.Trim();
        if (problem is null)
            errors.Add("Problem is required.");
        else if (problem.Length > ProblemMaxLength)
            errors.Add($"Problem must be at most {ProblemMaxLength} characters.");

        var reportedBy = string.IsNullOrWhiteSpace(request.ReportedBy) ? null : request.ReportedBy.Trim();
        if (reportedBy is { Length: > ReportedByMaxLength })
            errors.Add($"ReportedBy must be at most {ReportedByMaxLength} characters.");

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > DescriptionMaxLength })
            errors.Add($"Description must be at most {DescriptionMaxLength} characters.");

        // Priority: default to Medium when omitted.
        var priorityInput = string.IsNullOrWhiteSpace(request.Priority) ? null : request.Priority.Trim();
        var priority = priorityInput is null
            ? BreakdownPriority.Medium
            : BreakdownPriority.All.FirstOrDefault(p => string.Equals(p, priorityInput, StringComparison.OrdinalIgnoreCase));
        if (priority is null)
            errors.Add($"Priority must be one of: {string.Join(", ", BreakdownPriority.All)}.");

        if (errors.Count > 0)
            throw new ValidationException(errors);

        return new ValidCreateInput(
            request.MachineId!.Value,
            request.BreakdownDate!.Value,
            request.BreakdownTime!.Value,
            reportedBy,
            problem!,
            request.BreakdownTypeId is > 0 ? request.BreakdownTypeId : null,
            priority!,
            description);
    }

    private static (string NewStage, byte[] OriginalRowVersion) ValidateStageAdvance(
        MachineBreakdown existing, AdvanceMachineBreakdownStageRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Stage))
        {
            errors.Add("Stage is required.");
            throw new ValidationException(errors);
        }

        var newStage = request.Stage.Trim();

        // A Closed breakdown is final: refused like any other "already final" record in this project (409).
        if (existing.Stage == BreakdownStage.Closed)
        {
            throw new ConflictException("The breakdown is already closed and cannot be changed.");
        }

        if (!BreakdownStage.All.Contains(newStage))
        {
            errors.Add($"Stage must be one of: {string.Join(", ", BreakdownStage.All)}.");
            throw new ValidationException(errors);
        }

        var currentIdx = BreakdownStage.All.ToList().IndexOf(existing.Stage);
        var newIdx = BreakdownStage.All.ToList().IndexOf(newStage);

        if (newIdx != currentIdx + 1)
        {
            throw new ValidationException(
                $"Cannot advance from \"{existing.Stage}\" to \"{newStage}\". Stage must advance exactly one step.");
        }

        if (request.AssignedEngineerId is <= 0)
        {
            errors.Add("AssignedEngineerId is not valid.");
        }

        var typedName = request.AssignedToName?.Trim();
        if (!string.IsNullOrEmpty(typedName))
        {
            if (request.AssignedEngineerId is not null)
                errors.Add("Choose an employee OR type a name for Assigned To, not both.");
            if (typedName.Length > AssignedToNameMaxLength)
                errors.Add($"AssignedToName must be at most {AssignedToNameMaxLength} characters.");
        }

        if (request.RootCause?.Trim() is { Length: > RootCauseMaxLength })
        {
            errors.Add($"RootCause must be at most {RootCauseMaxLength} characters.");
        }

        if (request.CorrectiveAction?.Trim() is { Length: > CorrectiveActionMaxLength })
        {
            errors.Add($"CorrectiveAction must be at most {CorrectiveActionMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // RowVersion is required for optimistic concurrency.
        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            throw new ValidationException("RowVersion is required to update a breakdown.");
        }

        byte[] originalRowVersion;
        try
        {
            originalRowVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch
        {
            throw new ValidationException("RowVersion is not a valid base-64 string.");
        }

        return (newStage, originalRowVersion);
    }

    // ============================================================ Audit

    private async Task WriteAuditAsync(
        string action, MachineBreakdown breakdown, int? actingUserId, string? ipAddress,
        Func<string, string> describe, CancellationToken cancellationToken, IReadOnlyList<AuditLogDetailEntry>? details = null)
    {
        var actorName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actorName,
            Module = BreakdownModule,
            Action = action,
            EntityName = nameof(MachineBreakdown),
            EntityId = breakdown.MachineBreakdownId,
            RecordRef = breakdown.BreakdownNo,
            Description = describe(actorName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    // ============================================================ Machine status (BR-16 / BR-20)

    // Runs inside the repository's transaction on the LOCKED machine row; records what changed for the audit.
    private static void ApplyMachineStatus(Machine machine, string newStatus, DateTime now, int? actingUserId, MachineStatusChange change)
    {
        change.Before = machine.OperationalStatus;
        change.After = newStatus;
        if (newStatus != machine.OperationalStatus)
        {
            machine.OperationalStatus = newStatus;
            machine.UpdatedAt = now;
            machine.UpdatedBy = actingUserId;
        }
    }

    /// <summary>The machine status before/after the write - filled in inside the transaction, read after the commit.</summary>
    private sealed class MachineStatusChange
    {
        public string? Before { get; set; }
        public string? After { get; set; }
        private bool Changed => Before is not null && Before != After;

        public IReadOnlyList<AuditLogDetailEntry> Details(string machineCode) => Changed
            ? new[] { new AuditLogDetailEntry($"machine_operational_status ({machineCode})", Before, After) }
            : Array.Empty<AuditLogDetailEntry>();

        public string Summary(string machineCode) => Changed ? $" Machine {machineCode} status {Before} -> {After}." : string.Empty;
    }

    // ============================================================ Paging

    // The list is paged on the server; invalid values are refused instead of producing a negative OFFSET or an unbounded page.
    private static void ValidatePaging(MachineBreakdownListQuery query)
    {
        var errors = new List<string>();
        if (query.PageNumber < 1)
            errors.Add("PageNumber must be at least 1.");
        if (query.PageSize < 1 || query.PageSize > PaginationDefaults.MaxPageSize)
            errors.Add($"PageSize must be between 1 and {PaginationDefaults.MaxPageSize}.");

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    // ============================================================ Mapping

    private static MachineBreakdownDto MapToDto(MachineBreakdown b) => new()
    {
        MachineBreakdownId = b.MachineBreakdownId,
        BreakdownNo = b.BreakdownNo,
        MachineId = b.MachineId,
        MachineCode = b.Machine?.MachineCode ?? string.Empty,
        MachineName = b.Machine?.MachineName ?? string.Empty,
        MachineOperationalStatus = b.Machine?.OperationalStatus,
        BreakdownDate = b.BreakdownDate,
        BreakdownTime = b.BreakdownTime,
        ReportedBy = b.ReportedBy,
        Problem = b.Problem,
        BreakdownTypeId = b.BreakdownTypeId,
        BreakdownTypeName = b.BreakdownType?.BreakdownTypeName,
        Priority = b.Priority,
        Description = b.Description,
        Stage = b.Stage,
        AssignedEngineerId = b.AssignedEngineerId,
        AssignedEngineerName = b.AssignedEngineer?.EmployeeName,
        AssignedToName = b.AssignedToName,
        AssignedAt = b.AssignedAt,
        MaintenanceStartedAt = b.MaintenanceStartedAt,
        ResolvedAt = b.ResolvedAt,
        ClosedAt = b.ClosedAt,
        RootCause = b.RootCause,
        CorrectiveAction = b.CorrectiveAction,
        DowntimeHours = b.DowntimeHours,
        CreatedAt = b.CreatedAt,
        CreatedBy = b.CreatedBy,
        UpdatedAt = b.UpdatedAt,
        UpdatedBy = b.UpdatedBy,
        RowVersion = Convert.ToBase64String(b.RowVersion),
    };
}
