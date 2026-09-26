using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real MoldService against in-memory fakes. Acting user 1 "Sakthi". See <see cref="MoldTestData"/> for the molds,
/// products (3 inactive) and employees (3 inactive).
/// </summary>
public class MoldServiceTests
{
    private sealed record Sut(MoldService Service, InMemoryMoldRepository Molds, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var moldList = MoldTestData.Molds();
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var audit = new RecordingAuditLog();
        var evaluator = MoldPmTestFactory.Evaluator(new InMemoryMoldPmRepository(moldList), new InMemoryNotificationRepository(), new FixedClock(), auditOverride ?? audit);
        var service = new MoldService(molds, evaluator, products, employees, users, new FixedClock(), auditOverride ?? audit, NullLogger<MoldService>.Instance);
        return new Sut(service, molds, audit, users);
    }

    private static CreateMoldRequest NewMold(
        string name = "Bush Mold C", int productId = 2, int? personId = 2, string? serial = "BM-9",
        int? max = 500000, int? warning = 450000, int? replacement = 500000, string? status = "Available", int? cavities = 2) => new()
    {
        MoldName = name, ProductId = productId, MoldType = "Injection", CavityCount = cavities, Manufacturer = "Precision", SerialNumber = serial,
        Location = "MAC-0002", StorageLocation = "Rack B-2", CommissionDate = new DateOnly(2024, 6, 1), MaximumShots = max,
        WarningShots = warning, ReplacementShots = replacement, MaintenanceFrequencyShots = 50000, ResponsibleEmployeeId = personId,
        Status = status, Remarks = "New",
    };

    private static string Rv(InMemoryMoldRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request copying the stored mold ("no change"); `change` rewrites it.
    private static UpdateMoldRequest Edit(InMemoryMoldRepository repo, int id, Func<UpdateMoldRequest, UpdateMoldRequest>? change = null)
    {
        var s = repo.Stored(id);
        var request = new UpdateMoldRequest
        {
            MoldName = s.MoldName, ProductId = s.ProductId, MoldType = s.MoldType, CavityCount = s.CavityCount, Manufacturer = s.Manufacturer,
            SerialNumber = s.SerialNumber, Location = s.Location, StorageLocation = s.StorageLocation, CommissionDate = s.CommissionDate,
            MaximumShots = s.MaximumShots, WarningShots = s.WarningShots, ReplacementShots = s.ReplacementShots,
            MaintenanceFrequencyShots = s.MaintenanceFrequencyShots, CurrentUsageShots = s.CurrentUsageShots,
            ResponsibleEmployeeId = s.ResponsibleEmployeeId, Status = s.Status, Remarks = s.Remarks, RowVersion = Rv(repo, id),
        };
        return change is null ? request : change(request);
    }

    private static UpdateMoldRequest With(
        UpdateMoldRequest r, string? name = null, int? productId = null, int? personId = -1, string? serial = "keep", int? usage = -1,
        string? status = null, int? max = -1, int? warning = -1, int? replacement = -1) => new()
    {
        MoldName = name ?? r.MoldName, ProductId = productId ?? r.ProductId, MoldType = r.MoldType, CavityCount = r.CavityCount,
        Manufacturer = r.Manufacturer, SerialNumber = serial == "keep" ? r.SerialNumber : serial, Location = r.Location,
        StorageLocation = r.StorageLocation, CommissionDate = r.CommissionDate,
        MaximumShots = max == -1 ? r.MaximumShots : max, WarningShots = warning == -1 ? r.WarningShots : warning,
        ReplacementShots = replacement == -1 ? r.ReplacementShots : replacement, MaintenanceFrequencyShots = r.MaintenanceFrequencyShots,
        CurrentUsageShots = usage == -1 ? r.CurrentUsageShots : usage, ResponsibleEmployeeId = personId == -1 ? r.ResponsibleEmployeeId : personId,
        Status = status ?? r.Status, Remarks = r.Remarks, RowVersion = r.RowVersion,
    };

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_WithJoinedNames_AndTheLifeValues()
    {
        var result = await Create().Service.GetAllAsync(new MoldListQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Gasket Mold", "Old Mold", "Seal Mold A" }, result.Items.Select(i => i.MoldName));
        var seal = result.Items.Single(i => i.MoldCode == "MLD-0001");
        Assert.Equal("Rubber Seal A", seal.ProductName);
        Assert.Equal("PRD-0001", seal.ProductCode);
        Assert.Equal("Ravi Kumar", seal.ResponsibleEmployeeName);
        Assert.Equal("Normal", seal.LifeState);
        Assert.Equal(20, seal.LifeUsedPercent);         // 100k / 500k
        Assert.Equal(400000, seal.RemainingShots);
        var gasket = result.Items.Single(i => i.MoldCode == "MLD-0002");
        Assert.False(gasket.ProductIsActive);
        Assert.False(gasket.ResponsibleEmployeeIsActive);
        Assert.Equal("Warning", gasket.LifeState);
        Assert.Equal(92, gasket.LifeUsedPercent);       // 460k / 500k
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_Product_LifeState_AndSearch()
    {
        var s = Create();
        async Task<List<string>> Codes(MoldListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.MoldCode).ToList();

        Assert.Equal(new[] { "MLD-0003" }, await Codes(new MoldListQuery { Status = "Retired" }));
        Assert.Equal(new[] { "MLD-0002" }, await Codes(new MoldListQuery { ProductId = 3 }));
        Assert.Equal(new[] { "MLD-0003" }, await Codes(new MoldListQuery { LifeState = "Replace" }));
        Assert.Equal(new[] { "MLD-0001" }, await Codes(new MoldListQuery { Search = "seal" }));
        Assert.Equal(new[] { "MLD-0002" }, await Codes(new MoldListQuery { Search = "mld-0002" }));
        Assert.Empty(await Codes(new MoldListQuery { Status = "Lost" }));
    }

    [Fact]
    public async Task GetById_ReturnsEveryField_AndThrowsNotFoundForUnknown()
    {
        var s = Create();
        var dto = await s.Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("MLD-0001", dto.MoldCode);
        Assert.Equal("Compression", dto.MoldType);
        Assert.Equal(4, dto.CavityCount);
        Assert.Equal("SM-1", dto.SerialNumber);
        Assert.Equal("MAC-0001", dto.Location);
        Assert.Equal("Rack A-1", dto.StorageLocation);
        Assert.Equal(new DateOnly(2022, 1, 10), dto.CommissionDate);
        Assert.Equal(500000, dto.MaximumShots);
        Assert.Equal(450000, dto.WarningShots);
        Assert.Equal(500000, dto.ReplacementShots);
        Assert.Equal(50000, dto.MaintenanceFrequencyShots);
        Assert.Equal(100000, dto.CurrentUsageShots);
        Assert.Equal("In Production", dto.Status);
        Assert.Equal("Main", dto.Remarks);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
    }

    [Theory]
    [InlineData(0, 500000, 0)]
    [InlineData(250000, 500000, 50)]
    [InlineData(1, 3, 33)]
    [InlineData(1, 200, 1)]        // 0.5 rounds away from zero, as the template's Math.round does
    [InlineData(600000, 500000, 100)]
    public void LifeUsedPercent_FollowsTheAnalysisFormula(int usage, int max, int expected)
    {
        Assert.Equal(expected, MoldService.LifeUsedPercent(usage, max));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_StartsAtZeroUsage_SetsCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewMold(name: "  Bush Mold C ", status: "available"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("MLD-0004", dto.MoldCode);
        Assert.Equal("Bush Mold C", dto.MoldName);
        Assert.Equal("Available", dto.Status);           // normalized onto the CK value
        Assert.Equal(0, dto.CurrentUsageShots);
        Assert.Equal("Normal", dto.LifeState);
        Assert.Equal(0, dto.LifeUsedPercent);
        Assert.Equal(500000, dto.RemainingShots);
        Assert.Equal("Gasket Set D", dto.ProductName);
        Assert.Equal("Karthik Raja", dto.ResponsibleEmployeeName);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MoldCreated", entry.Action);
        Assert.Equal("Mold", entry.Module);
        Assert.Equal("Mold", entry.EntityName);
        Assert.Equal(dto.MoldId, entry.EntityId);
        Assert.Equal("MLD-0004", entry.RecordRef);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Create_DefaultsCavitiesToOne_AndAllowsNoPersonOrSerial()
    {
        var dto = await Create().Service.CreateAsync(NewMold(personId: null, serial: " ", cavities: null), 1, null, CancellationToken.None);

        Assert.Equal(1, dto.CavityCount);
        Assert.Null(dto.ResponsibleEmployeeId);
        Assert.Null(dto.SerialNumber);
    }

    [Fact]
    public async Task Create_RequiresTheBR06FieldsAndStatus_WithAllErrors_WritingNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(
            new CreateMoldRequest { MoldName = " ", ProductId = 0, MoldType = "" }, 1, null, CancellationToken.None));

        foreach (var expected in new[] { "MoldName is required.", "ProductId is required.", "MoldType is required.", "MaximumShots is required.", "WarningShots is required.", "ReplacementShots is required.", "Status is required." })
        {
            Assert.Contains(expected, ex.Errors);
        }

        Assert.Equal(0, s.Molds.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Theory]
    [InlineData(0, 0, 0, "Maximum shots must be greater than 0.")]
    [InlineData(1000, 1000, 1000, "Warning level must be less than maximum life.")]
    [InlineData(1000, 800, 800, "Replacement level must be greater than warning level.")]
    [InlineData(1000, 800, 1200, "Replacement level cannot exceed maximum shots.")]
    public async Task Create_EnforcesTheLifeThresholdCheckConstraints(int max, int warning, int replacement, string expected)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create().Service.CreateAsync(NewMold(max: max, warning: warning, replacement: replacement), 1, null, CancellationToken.None));

        Assert.Contains(expected, ex.Errors);
    }

    [Fact]
    public async Task Create_RejectsZeroCavities_AnUnknownStatus_AndOverlongFields()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateMoldRequest
        {
            MoldName = new string('n', 151), ProductId = 1, MoldType = new string('t', 101), CavityCount = 0, Manufacturer = new string('m', 101),
            SerialNumber = new string('s', 101), Location = new string('l', 101), StorageLocation = new string('r', 101), Remarks = new string('x', 501),
            MaximumShots = 10, WarningShots = 5, ReplacementShots = 10, Status = "Lost",
        }, 1, null, CancellationToken.None));

        foreach (var expected in new[]
        {
            "MoldName must be at most 150 characters.", "MoldType must be at most 100 characters.", "CavityCount must be at least 1.",
            "Manufacturer must be at most 100 characters.", "SerialNumber must be at most 100 characters.", "Location must be at most 100 characters.",
            "StorageLocation must be at most 100 characters.", "Remarks must be at most 500 characters.",
            "Status must be one of: Available, In Production, Maintenance, Replacement Due, Retired.",
        })
        {
            Assert.Contains(expected, ex.Errors);
        }
    }

    [Fact]
    public async Task Create_UnknownProductOrPerson_IsNotFound_InactiveOnes_AreValidationErrors()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewMold(productId: 999), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewMold(personId: 999), 1, null, CancellationToken.None));
        var product = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewMold(productId: 3), 1, null, CancellationToken.None));
        Assert.Contains("The selected product is not active.", product.Errors);
        var person = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewMold(personId: 3), 1, null, CancellationToken.None));
        Assert.Contains("The selected responsible person is not active.", person.Errors);

        Assert.Equal(0, s.Molds.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RefusesADuplicateNonRetiredName_AndATakenSerial_ButAllowsARetiredMoldsName()
    {
        var s = Create();

        var name = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewMold(name: "seal mold a"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", name.Message);
        var serial = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewMold(serial: "sm-1"), 1, null, CancellationToken.None));
        Assert.Contains("serial number", serial.Message);
        Assert.Empty(s.Audit.Entries);

        var reuse = await s.Service.CreateAsync(NewMold(name: "Old Mold"), 1, null, CancellationToken.None);
        Assert.Equal("Old Mold", reuse.MoldName);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesTheEditableFields_RecomputesTheLifeState_AndKeepsTheCode()
    {
        var s = Create();
        var oldVersion = Rv(s.Molds, 1);

        var dto = await s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, name: "Seal Mold A2", productId: 2, personId: 2, usage: 460000, status: "Maintenance")), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Seal Mold A2", dto.MoldName);
        Assert.Equal("Gasket Set D", dto.ProductName);
        Assert.Equal("Karthik Raja", dto.ResponsibleEmployeeName);
        Assert.Equal(460000, dto.CurrentUsageShots);
        Assert.Equal("Warning", dto.LifeState);          // recomputed from the new usage
        Assert.Equal("Maintenance", dto.Status);
        var stored = s.Molds.Stored(1);
        Assert.Equal("MLD-0001", stored.MoldCode);
        Assert.Equal(1, stored.CreatedBy);
        Assert.Equal(1, stored.UpdatedBy);
        Assert.Equal(FixedClock.Now, stored.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);
    }

    [Fact]
    public async Task Update_WithoutAUsageValue_KeepsTheStoredUsage()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, usage: null)), 1, null, CancellationToken.None);

        Assert.Equal(100000, dto.CurrentUsageShots);
        Assert.Equal(100000, s.Molds.Stored(1).CurrentUsageShots);
    }

    [Fact]
    public async Task Update_AuditsFieldLevelChanges_IncludingTheUsageCorrectionWithOldAndNewValues()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, productId: 2, personId: null, usage: 120000, status: "Available")), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MoldUpdated", entry.Action);
        Assert.Equal("MLD-0001", entry.RecordRef);
        Assert.Contains("Changed: product_code, current_usage_shots, responsible_employee_code, status.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "current_usage_shots", OldValue: "100000", NewValue: "120000" });
        Assert.Contains(entry.Details, d => d is { FieldName: "product_code", OldValue: "PRD-0001", NewValue: "PRD-0002" });
        Assert.Contains(entry.Details, d => d is { FieldName: "responsible_employee_code", OldValue: "EMP-0001", NewValue: null });
        Assert.Contains(entry.Details, d => d is { FieldName: "status", OldValue: "In Production", NewValue: "Available" });
    }

    [Fact]
    public async Task Update_RejectsANegativeUsage_AndInvalidThresholds()
    {
        var s = Create();

        var usage = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, usage: -5)), 1, null, CancellationToken.None));
        Assert.Contains("CurrentUsageShots cannot be negative.", usage.Errors);
        var thresholds = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, max: 400000)), 1, null, CancellationToken.None));
        Assert.Contains("Warning level must be less than maximum life.", thresholds.Errors);
        Assert.Contains("Replacement level cannot exceed maximum shots.", thresholds.Errors);

        Assert.Equal(0, s.Molds.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_KeepingInactiveReferences_IsAllowed_ButChoosingThemIsNot()
    {
        var s = Create();

        // Mold 2's product (3) and person (3) have both been deactivated: an unrelated edit still saves.
        var kept = await s.Service.UpdateAsync(2, Edit(s.Molds, 2, r => With(r, name: "Gasket Mold v2")), 1, null, CancellationToken.None);
        Assert.Equal(3, kept.ProductId);
        Assert.False(kept.ProductIsActive);
        Assert.Equal(3, kept.ResponsibleEmployeeId);

        var product = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, productId: 3)), 1, null, CancellationToken.None));
        Assert.Contains("The selected product is not active.", product.Errors);
        var person = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, personId: 3)), 1, null, CancellationToken.None));
        Assert.Contains("The selected responsible person is not active.", person.Errors);
        Assert.Equal(1, s.Molds.Stored(1).ProductId);
    }

    [Fact]
    public async Task Update_UnknownMoldOrReferences_AreNotFound_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s.Molds, 1), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, productId: 999)), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => With(r, personId: 999)), 1, null, CancellationToken.None));

        Assert.Equal(0, s.Molds.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_DuplicateNameOrSerial_IsAConflict_AndReturningARetiredMoldToServiceChecksTheName()
    {
        var s = Create();

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s.Molds, 2, r => With(r, name: "SEAL MOLD A")), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s.Molds, 2, r => With(r, serial: "SM-1")), 1, null, CancellationToken.None));

        // Mold 3 (Retired) renamed to an in-use name is fine while it stays retired, but not when it returns to service.
        await s.Service.UpdateAsync(3, Edit(s.Molds, 3, r => With(r, name: "Seal Mold A")), 1, null, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(3, Edit(s.Molds, 3, r => With(r, status: "Available")), 1, null, CancellationToken.None));
        Assert.Equal("Retired", s.Molds.Stored(3).Status);
    }

    [Fact]
    public async Task Update_ValidatesTheRowVersion_AndRefusesAStaleOne()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Molds, 1, r => new UpdateMoldRequest
        {
            MoldName = r.MoldName, ProductId = r.ProductId, MoldType = r.MoldType, MaximumShots = r.MaximumShots, WarningShots = r.WarningShots,
            ReplacementShots = r.ReplacementShots, Status = r.Status, RowVersion = "",
        }), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is required.", missing.Errors);

        var stale = Edit(s.Molds, 1, r => With(r, name: "Stale edit"));
        s.Molds.SimulateConcurrentModification(1);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Equal("The mold was modified by another user. Refresh the mold and try again.", ex.Message);
        Assert.Equal("Seal Mold A", s.Molds.Stored(1).MoldName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate (= retire)

    [Fact]
    public async Task Deactivate_SetsStatusRetired_KeepsEverythingElse_AndAudits()
    {
        var s = Create();
        var before = s.Molds.Stored(1);
        var snapshot = (before.MoldCode, before.MoldName, before.ProductId, before.CurrentUsageShots, before.MaximumShots, before.CreatedBy);

        var dto = await s.Service.DeactivateAsync(1, 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Retired", dto.Status);
        var after = s.Molds.Stored(1);
        Assert.Equal("Retired", after.Status);
        Assert.Equal(snapshot, (after.MoldCode, after.MoldName, after.ProductId, after.CurrentUsageShots, after.MaximumShots, after.CreatedBy));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(3, s.Molds.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MoldDeactivated", entry.Action);
        Assert.Equal("MLD-0001", entry.RecordRef);
        Assert.Contains(entry.Details, d => d is { FieldName: "status", OldValue: "In Production", NewValue: "Retired" });
    }

    [Fact]
    public async Task Deactivate_UnknownIsNotFound_AlreadyRetiredIsAConflict_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Equal("The mold is already retired.", ex.Message);
        Assert.Equal(0, s.Molds.RetireCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict()
    {
        var s = Create();
        s.Molds.BeforeWrite = s.Molds.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.Equal("In Production", s.Molds.Stored(1).Status);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewMold(), 1, null, CancellationToken.None);

        Assert.Equal("MLD-0004", dto.MoldCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewMold(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.MoldId, Edit(s.Molds, created.MoldId, r => With(r, name: "Bush Mold D")), 1, null, CancellationToken.None);
        var retired = await s.Service.DeactivateAsync(created.MoldId, 1, null, CancellationToken.None);

        Assert.Equal("Bush Mold D", updated.MoldName);
        Assert.Equal("Retired", retired.Status);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
