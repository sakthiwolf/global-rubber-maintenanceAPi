using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Unit tests for MachineBreakdownService against in-memory fakes. Acting user 1 "Sakthi".
/// Every test re-creates the SUT to keep tests isolated.
/// </summary>
public class MachineBreakdownServiceTests
{
    private static readonly DateOnly ReportDay = new(2026, 9, 25);
    private static readonly TimeOnly ReportTime = new(9, 30);

    private sealed record Sut(
        MachineBreakdownService Service,
        InMemoryMachineBreakdownRepository Breakdowns,
        RecordingAuditLog Audit);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var breakdownList = MachineBreakdownTestData.Breakdowns();
        var breakdowns = new InMemoryMachineBreakdownRepository(breakdownList, MachineTestData.Machines());
        var audit = new RecordingAuditLog();
        var service = new MachineBreakdownService(
            breakdowns, machines, users, new FixedClock(),
            auditOverride ?? audit, NullLogger<MachineBreakdownService>.Instance);
        return new Sut(service, breakdowns, audit);
    }

    private static CreateMachineBreakdownRequest Req(
        int? machineId = 1,
        DateOnly? date = null,
        TimeOnly? time = null,
        string? reportedBy = "Test User",
        string? problem = "Spindle noise",
        int? breakdownTypeId = null,
        string? priority = null,
        string? description = null) => new()
    {
        MachineId = machineId,
        BreakdownDate = date ?? ReportDay,
        BreakdownTime = time ?? ReportTime,
        ReportedBy = reportedBy,
        Problem = problem,
        BreakdownTypeId = breakdownTypeId,
        Priority = priority,
        Description = description,
    };

    // ========================================================= Create

    [Fact]
    public async Task Create_IssuesBreakdownNumber_SetsReportedStage_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(reportedBy: " John Doe "), 1, "10.0.0.1", CancellationToken.None);

        Assert.Equal("BRK-0003", dto.BreakdownNo);  // seeds had 2 existing breakdowns
        Assert.Equal(BreakdownStage.Reported, dto.Stage);
        Assert.Equal("John Doe", dto.ReportedBy);   // trimmed
        Assert.Equal("Spindle noise", dto.Problem);
        Assert.Equal(BreakdownPriority.Medium, dto.Priority);
        Assert.Equal(1, dto.CreatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal(BreakdownAuditNames.Created, entry.Action);
        Assert.Equal("Machine Breakdown", entry.Module);
        Assert.Equal("BRK-0003", entry.RecordRef);
    }

    [Fact]
    public async Task Create_ReportedBy_IsStoredAsTyped_NotLookedUpInEmployeeMaster()
    {
        // The value is stored verbatim (trimmed), never resolved to an employee.
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(reportedBy: "  Someone Not In System  "), 1, null, CancellationToken.None);

        Assert.Equal("Someone Not In System", dto.ReportedBy);
    }

    [Fact]
    public async Task Create_NullReportedBy_IsAllowed()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(reportedBy: null), 1, null, CancellationToken.None);

        Assert.Null(dto.ReportedBy);
    }

    [Fact]
    public async Task Create_OmittedPriority_DefaultsToMedium()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(priority: null), 1, null, CancellationToken.None);

        Assert.Equal(BreakdownPriority.Medium, dto.Priority);
    }

    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public async Task Create_ValidPriority_IsAccepted(string priority)
    {
        var dto = await Create().Service.CreateAsync(Req(priority: priority), 1, null, CancellationToken.None);

        Assert.Equal(priority, dto.Priority);
    }

    [Fact]
    public async Task Create_InvalidPriority_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Create().Service.CreateAsync(Req(priority: "Extreme"), 1, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("Priority"));
    }

    [Fact]
    public async Task Create_MissingMachineId_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Create().Service.CreateAsync(Req(machineId: null), 1, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("MachineId"));
    }

    [Fact]
    public async Task Create_MissingProblem_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => Create().Service.CreateAsync(Req(problem: "   "), 1, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.Contains("Problem"));
    }

    [Fact]
    public async Task Create_InactiveMachine_ThrowsValidation()
    {
        // MachineTestData machine 3 is inactive.
        await Assert.ThrowsAsync<ValidationException>(
            () => Create().Service.CreateAsync(Req(machineId: 3), 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_UnknownMachine_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Create().Service.CreateAsync(Req(machineId: 9999), 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_NumbersAreSequential()
    {
        var s = Create();

        var a = await s.Service.CreateAsync(Req(), 1, null, CancellationToken.None);
        var b = await s.Service.CreateAsync(Req(), 1, null, CancellationToken.None);

        Assert.Equal(("BRK-0003", "BRK-0004"), (a.BreakdownNo, b.BreakdownNo));
    }

    // ========================================================= AdvanceStage

    [Fact]
    public async Task AdvanceStage_ReportedToAssigned_SetsAssignedAt_AndAudits()
    {
        var s = Create();

        // breakdown 1 is in Reported stage (seeded)
        var existing = await s.Service.GetByIdAsync(1, CancellationToken.None);
        var dto = await s.Service.AdvanceStageAsync(1, new AdvanceMachineBreakdownStageRequest
        {
            Stage = BreakdownStage.Assigned,
            RowVersion = existing.RowVersion,
        }, 1, null, CancellationToken.None);

        Assert.Equal(BreakdownStage.Assigned, dto.Stage);
        Assert.NotNull(dto.AssignedAt);
        Assert.NotNull(dto.UpdatedAt);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal(BreakdownAuditNames.StageChanged, entry.Action);
        Assert.Contains("Reported", entry.Description);
        Assert.Contains("Assigned", entry.Description);
    }

    [Fact]
    public async Task AdvanceStage_AssignedToMaintenanceStarted_Works()
    {
        var s = Create();

        var breakdown2 = await s.Service.GetByIdAsync(2, CancellationToken.None);
        var dto = await s.Service.AdvanceStageAsync(2, new AdvanceMachineBreakdownStageRequest
        {
            Stage = BreakdownStage.MaintenanceStarted,
            RowVersion = breakdown2.RowVersion,
        }, 1, null, CancellationToken.None);

        Assert.Equal(BreakdownStage.MaintenanceStarted, dto.Stage);
        Assert.NotNull(dto.MaintenanceStartedAt);
    }

    [Fact]
    public async Task AdvanceStage_SkippingAStage_ThrowsValidation()
    {
        var s = Create();

        var existing = await s.Service.GetByIdAsync(1, CancellationToken.None);
        await Assert.ThrowsAsync<ValidationException>(
            () => s.Service.AdvanceStageAsync(1, new AdvanceMachineBreakdownStageRequest
            {
                Stage = BreakdownStage.MaintenanceStarted, // skips Assigned
                RowVersion = existing.RowVersion,
            }, 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task AdvanceStage_StaleRowVersion_ThrowsConflict()
    {
        var s = Create();

        var staleRowVersion = Convert.ToBase64String(new byte[] { 0xFF, 0xFF }); // wrong
        await Assert.ThrowsAsync<ConflictException>(
            () => s.Service.AdvanceStageAsync(1, new AdvanceMachineBreakdownStageRequest
            {
                Stage = BreakdownStage.Assigned,
                RowVersion = staleRowVersion,
            }, 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task AdvanceStage_MissingRowVersion_ThrowsValidation()
    {
        var s = Create();

        await Assert.ThrowsAsync<ValidationException>(
            () => s.Service.AdvanceStageAsync(1, new AdvanceMachineBreakdownStageRequest
            {
                Stage = BreakdownStage.Assigned,
                RowVersion = null,
            }, 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task AdvanceStage_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Create().Service.AdvanceStageAsync(9999, new AdvanceMachineBreakdownStageRequest
            {
                Stage = BreakdownStage.Assigned,
                RowVersion = Convert.ToBase64String(new byte[] { 1 }),
            }, 1, null, CancellationToken.None));
    }

    // ========================================================= GetAll / GetById

    [Fact]
    public async Task GetAll_ReturnsPagedResult()
    {
        var s = Create();

        var page = await s.Service.GetAllAsync(
            new MachineBreakdownListQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task GetAll_FiltersByStage()
    {
        var s = Create();

        var page = await s.Service.GetAllAsync(
            new MachineBreakdownListQuery { PageNumber = 1, PageSize = 10, Stage = BreakdownStage.Reported },
            CancellationToken.None);

        Assert.Equal(1, page.TotalCount);
        Assert.All(page.Items, b => Assert.Equal(BreakdownStage.Reported, b.Stage));
    }

    [Fact]
    public async Task GetById_ReturnsDto()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("BRK-0001", dto.BreakdownNo);
        Assert.Equal("Test Reporter", dto.ReportedBy);
    }

    [Fact]
    public async Task GetById_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => Create().Service.GetByIdAsync(9999, CancellationToken.None));
    }
}
