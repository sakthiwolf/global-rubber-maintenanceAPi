using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
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
///  - BreakdownTypeId, if supplied, must reference an existing breakdown type.
///  - Initial Stage = Reported.
///  - Stage advances forward only: Reported → Assigned → Maintenance Started → Resolved → Closed.
/// </summary>
public sealed class MachineBreakdownService : IMachineBreakdownService
{
    private const string BreakdownModule = "Machine Breakdown";
    private const int ProblemMaxLength = 500;
    private const int ReportedByMaxLength = 100;
    private const int DescriptionMaxLength = 1000;

    private readonly IMachineBreakdownRepository _repository;
    private readonly IMachineRepository _machineRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MachineBreakdownService> _logger;

    public MachineBreakdownService(
        IMachineBreakdownRepository repository,
        IMachineRepository machineRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<MachineBreakdownService> logger)
    {
        _repository = repository;
        _machineRepository = machineRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<MachineBreakdownDto>> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken)
    {
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

        var created = await _repository.AddAsync(breakdown, cancellationToken);
        created.Machine = machine;

        await WriteAuditAsync(
            BreakdownAuditNames.Created, created, actingUserId, ipAddress,
            actor => $"{actor} reported breakdown {created.BreakdownNo} for machine {machine.MachineCode}. Problem: {input.Problem}",
            cancellationToken);

        return MapToDto(created);
    }

    public async Task<MachineBreakdownDto> AdvanceStageAsync(
        int machineBreakdownId, AdvanceMachineBreakdownStageRequest request,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetByIdAsync(machineBreakdownId, cancellationToken)
            ?? throw new NotFoundException(nameof(MachineBreakdown), machineBreakdownId);

        var (newStage, originalRowVersion) = ValidateStageAdvance(existing, request);

        var now = _dateTimeProvider.UtcNow;
        var oldStage = existing.Stage;

        existing.Stage = newStage;
        existing.UpdatedAt = now;
        existing.UpdatedBy = actingUserId;

        switch (newStage)
        {
            case BreakdownStage.Assigned:
                existing.AssignedEngineerId = request.AssignedEngineerId;
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

        var updated = await _repository.UpdateStageAsync(existing, originalRowVersion, cancellationToken);

        await WriteAuditAsync(
            BreakdownAuditNames.StageChanged, updated, actingUserId, ipAddress,
            actor => $"{actor} moved breakdown {updated.BreakdownNo} from \"{oldStage}\" to \"{newStage}\".",
            cancellationToken);

        return MapToDto(updated);
    }

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
        Func<string, string> describe, CancellationToken cancellationToken)
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
        }, cancellationToken);
    }

    // ============================================================ Mapping

    private static MachineBreakdownDto MapToDto(MachineBreakdown b) => new()
    {
        MachineBreakdownId = b.MachineBreakdownId,
        BreakdownNo = b.BreakdownNo,
        MachineId = b.MachineId,
        MachineCode = b.Machine?.MachineCode ?? string.Empty,
        MachineName = b.Machine?.MachineName ?? string.Empty,
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
