using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real SparePartService against in-memory fakes. Acting user 1 "Sakthi". See <see cref="SparePartTestData"/> for the
/// spare parts, machines (3 inactive) and vendors (3 inactive).
/// </summary>
public class SparePartServiceTests
{
    private sealed record Sut(SparePartService Service, InMemorySparePartRepository Parts, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var parts = new InMemorySparePartRepository(SparePartTestData.SpareParts(), machines, vendors);
        var audit = new RecordingAuditLog();
        var service = new SparePartService(parts, machines, vendors, users, new FixedClock(), auditOverride ?? audit, NullLogger<SparePartService>.Instance);
        return new Sut(service, parts, audit, users);
    }

    private static CreateSparePartRequest NewPart(
        string name = "V-Belt B42", int? machineId = 2, int? vendorId = 2, string unit = "Nos",
        int? minimum = 4, int? current = 10, decimal? cost = 350.75m) => new()
    {
        SparePartName = name, Category = "Belts", MachineId = machineId, PartNumber = "VB-42", Unit = unit, MinimumStock = minimum,
        CurrentStock = current, VendorId = vendorId, StoreLocation = "Rack B-3", UnitCost = cost,
    };

    private static string Rv(InMemorySparePartRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request copying the stored spare part ("no change") with optional overrides. -5 = "keep" for the nullable ints.
    private static UpdateSparePartRequest Edit(
        InMemorySparePartRepository repo, int id, string? name = null, int? machineId = -5, int? vendorId = -5,
        int? minimum = -5, int? current = -5, decimal? cost = -5m, string? unit = null, string? rowVersion = null)
    {
        var s = repo.Stored(id);
        return new UpdateSparePartRequest
        {
            SparePartName = name ?? s.SparePartName, Category = s.Category, MachineId = machineId == -5 ? s.MachineId : machineId,
            PartNumber = s.PartNumber, Unit = unit ?? s.Unit, MinimumStock = minimum == -5 ? s.MinimumStock : minimum,
            CurrentStock = current == -5 ? s.CurrentStock : current, VendorId = vendorId == -5 ? s.VendorId : vendorId,
            StoreLocation = s.StoreLocation, UnitCost = cost == -5m ? s.UnitCost : cost, RowVersion = rowVersion ?? Rv(repo, id),
        };
    }

    private static async Task<List<string>> ValidationErrors(Func<Task> act) =>
        (await Assert.ThrowsAsync<ValidationException>(act)).Errors.ToList();

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_WithJoinedNames_AndTheStoredStockStatus()
    {
        var result = await Create().Service.GetAllAsync(new SparePartListQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Heater Band", "Hydraulic Seal Kit", "Old Bearing" }, result.Items.Select(i => i.SparePartName));
        var kit = result.Items.Single(i => i.SparePartCode == "SPR-0001");
        Assert.Equal("MAC-0001", kit.MachineCode);
        Assert.Equal("Injection Moulding M/c 1", kit.MachineName);
        Assert.True(kit.MachineIsActive);
        Assert.Equal("Chennai Hydraulics", kit.VendorName);
        Assert.True(kit.VendorIsActive);
        Assert.Equal("Available", kit.StockStatus);
        Assert.Equal(1250.50m, kit.UnitCost);
        var band = result.Items.Single(i => i.SparePartCode == "SPR-0002");
        Assert.False(band.MachineIsActive);
        Assert.False(band.VendorIsActive);
        Assert.Equal("Low Stock", band.StockStatus);
        var bearing = result.Items.Single(i => i.SparePartCode == "SPR-0003");
        Assert.Null(bearing.MachineId);
        Assert.Null(bearing.MachineIsActive);
        Assert.Null(bearing.VendorIsActive);
        Assert.False(bearing.IsActive);
    }

    [Fact]
    public async Task GetAll_HonoursTheFilters()
    {
        var s = Create();
        async Task<int> Count(SparePartListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).TotalCount;

        Assert.Equal(1, await Count(new SparePartListQuery { Search = "seal" }));
        Assert.Equal(1, await Count(new SparePartListQuery { Search = "spr-0002" }));
        Assert.Equal(1, await Count(new SparePartListQuery { StockStatus = "Out of Stock" }));
        Assert.Equal(0, await Count(new SparePartListQuery { StockStatus = "Nonsense" }));
        Assert.Equal(1, await Count(new SparePartListQuery { MachineId = 3 }));
        Assert.Equal(2, await Count(new SparePartListQuery { IsActive = true }));
        Assert.Equal(1, await Count(new SparePartListQuery { IsActive = false }));
    }

    [Fact]
    public async Task GetById_Returns_OrThrowsNotFound()
    {
        var s = Create();

        Assert.Equal("Hydraulic Seal Kit", (await s.Service.GetByIdAsync(1, CancellationToken.None)).SparePartName);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_StoresTheFields_ReadsBackTheStockStatus_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewPart(), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("SPR-0004", dto.SparePartCode);
        Assert.Equal("Available", dto.StockStatus);
        Assert.Equal("MAC-0002", dto.MachineCode);
        Assert.Equal("Precision Mold Tech", dto.VendorName);
        Assert.True(dto.IsActive);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Equal(1, dto.CreatedBy);
        var stored = s.Parts.Stored(dto.SparePartId);
        Assert.Equal(("V-Belt B42", 4, 10, 350.75m), (stored.SparePartName, stored.MinimumStock, stored.CurrentStock, stored.UnitCost));
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal(("SparePartCreated", "Spare Part", "SparePart", "SPR-0004"), (entry.Action, entry.Module, entry.EntityName, entry.RecordRef));
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Sakthi", entry.Description);
    }

    [Theory]
    [InlineData(0, 0, "Out of Stock")] // 0 is a valid stock (fixes D-04)
    [InlineData(5, 5, "Low Stock")]    // current <= minimum
    [InlineData(5, 6, "Available")]
    [InlineData(0, 1, "Available")]
    public async Task Create_AcceptsZero_AndTheStatusFollowsBR29(int minimum, int current, string expected)
    {
        var dto = await Create().Service.CreateAsync(NewPart(minimum: minimum, current: current), 1, null, CancellationToken.None);

        Assert.Equal(expected, dto.StockStatus);
        Assert.Equal(current, dto.CurrentStock);
    }

    [Fact]
    public async Task Create_DoesNotRelateMinimumAndCurrentStock()
    {
        // No rule in the analysis/DB ties the two together: current below minimum is simply Low Stock.
        var dto = await Create().Service.CreateAsync(NewPart(minimum: 100, current: 3), 1, null, CancellationToken.None);

        Assert.Equal("Low Stock", dto.StockStatus);
    }

    [Fact]
    public async Task Create_WithoutMachineOrVendorOrCost_IsAllowed_AndBlankOptionalsBecomeNull()
    {
        var s = Create();
        var request = new CreateSparePartRequest
        {
            SparePartName = "  Grease  ", Category = "  ", PartNumber = "", Unit = " Kg ", MinimumStock = 0, CurrentStock = 0, StoreLocation = " ",
        };

        var dto = await s.Service.CreateAsync(request, 1, null, CancellationToken.None);

        var stored = s.Parts.Stored(dto.SparePartId);
        Assert.Equal(("Grease", "Kg"), (stored.SparePartName, stored.Unit));
        Assert.Null(stored.Category);
        Assert.Null(stored.PartNumber);
        Assert.Null(stored.StoreLocation);
        Assert.Null(stored.MachineId);
        Assert.Null(stored.VendorId);
        Assert.Null(stored.UnitCost);
        Assert.Null(dto.MachineCode);
        Assert.Null(dto.VendorName);
    }

    [Fact]
    public async Task Create_ReportsEveryFieldError_AndWritesNothing()
    {
        var s = Create();
        var request = new CreateSparePartRequest
        {
            SparePartName = " ", Unit = "", MinimumStock = null, CurrentStock = null, Category = new string('c', 101),
            PartNumber = new string('p', 51), StoreLocation = new string('l', 101), UnitCost = 1.234m, MachineId = 0, VendorId = -1,
        };

        var errors = await ValidationErrors(() => s.Service.CreateAsync(request, 1, null, CancellationToken.None));

        Assert.Equal(new[]
        {
            "SparePartName is required.", "Category must be at most 100 characters.", "PartNumber must be at most 50 characters.",
            "Unit is required.", "StoreLocation must be at most 100 characters.", "MinimumStock is required.", "CurrentStock is required.",
            "MachineId is not valid.", "VendorId is not valid.", "UnitCost can have at most 2 decimal places.",
        }, errors);
        Assert.Equal(0, s.Parts.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsNegativeStocksAndCost_AndOverlongNameAndUnit()
    {
        var s = Create();

        var errors = await ValidationErrors(() => s.Service.CreateAsync(
            NewPart(name: new string('n', 151), unit: new string('u', 21), minimum: -1, current: -1, cost: -0.01m), 1, null, CancellationToken.None));

        Assert.Contains("SparePartName must be at most 150 characters.", errors);
        Assert.Contains("Unit must be at most 20 characters.", errors);
        Assert.Contains("MinimumStock cannot be negative.", errors);
        Assert.Contains("CurrentStock cannot be negative.", errors);
        Assert.Contains("UnitCost cannot be negative.", errors);
        Assert.Equal(0, s.Parts.AddCalls);
    }

    [Fact]
    public async Task Create_AcceptsTheLimits()
    {
        var dto = await Create().Service.CreateAsync(
            NewPart(name: new string('n', 150), unit: new string('u', 20), cost: 0m), 1, null, CancellationToken.None);

        Assert.Equal(0m, dto.UnitCost);
    }

    [Fact]
    public async Task Create_RejectsAMissingOrInactiveMachineOrVendor()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewPart(machineId: 999), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewPart(vendorId: 999), 1, null, CancellationToken.None));
        Assert.Equal(new[] { "The selected machine is not active." },
            await ValidationErrors(() => s.Service.CreateAsync(NewPart(machineId: 3), 1, null, CancellationToken.None)));
        Assert.Equal(new[] { "The selected supplier is not active." },
            await ValidationErrors(() => s.Service.CreateAsync(NewPart(vendorId: 3), 1, null, CancellationToken.None)));
        Assert.Equal(0, s.Parts.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsANameUsedByAnActivePart_ButNotByAnInactiveOne()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewPart(name: " hydraulic SEAL kit "), 1, null, CancellationToken.None));
        Assert.Equal("A spare part named 'hydraulic SEAL kit' already exists.", ex.Message);

        var reused = await s.Service.CreateAsync(NewPart(name: "Old Bearing"), 1, null, CancellationToken.None); // SPR-0003 is inactive
        Assert.Equal("Old Bearing", reused.SparePartName);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_SavesTheFields_IncludingCurrentStock_RecomputesTheStatus_AndAuditsOldAndNew()
    {
        var s = Create();
        var oldVersion = Rv(s.Parts, 1);

        var dto = await s.Service.UpdateAsync(1, Edit(s.Parts, 1, name: "Hydraulic Seal Kit B", current: 0, cost: 1300m, machineId: 2, vendorId: null), 1, null, CancellationToken.None);

        Assert.Equal("SPR-0001", dto.SparePartCode);
        Assert.Equal(0, dto.CurrentStock);
        Assert.Equal("Out of Stock", dto.StockStatus);
        Assert.Equal("MAC-0002", dto.MachineCode);
        Assert.Null(dto.VendorId);
        Assert.NotEqual(oldVersion, dto.RowVersion);
        var after = s.Parts.Stored(1);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(1, after.CreatedBy);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("SparePartUpdated", entry.Action);
        Assert.Contains(entry.Details, d => d is { FieldName: "spare_part_name", OldValue: "Hydraulic Seal Kit", NewValue: "Hydraulic Seal Kit B" });
        Assert.Contains(entry.Details, d => d is { FieldName: "current_stock", OldValue: "12", NewValue: "0" });
        Assert.Contains(entry.Details, d => d is { FieldName: "unit_cost", OldValue: "1250.50", NewValue: "1300.00" });
        Assert.Contains(entry.Details, d => d is { FieldName: "machine_code", OldValue: "MAC-0001", NewValue: "MAC-0002" });
        Assert.Contains(entry.Details, d => d is { FieldName: "vendor_name", OldValue: "Chennai Hydraulics", NewValue: null });
        Assert.Contains(entry.Details, d => d is { FieldName: "stock_status", OldValue: "Available", NewValue: "Out of Stock" });
        Assert.DoesNotContain(entry.Details, d => d.FieldName == "minimum_stock");
    }

    [Fact]
    public async Task Update_WithNoChanges_StillSaves_AndSaysSo()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Parts, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Empty(entry.Details);
        Assert.Contains("No field values changed.", entry.Description);
    }

    [Fact]
    public async Task Update_KeepsAnUnchangedInactiveMachineAndVendor()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(2, Edit(s.Parts, 2, current: 20), 1, null, CancellationToken.None);

        Assert.Equal((3, 3), (dto.MachineId!.Value, dto.VendorId!.Value));
        Assert.False(dto.MachineIsActive);
        Assert.False(dto.VendorIsActive);
        Assert.Equal("Available", dto.StockStatus);
    }

    [Fact]
    public async Task Update_RejectsANewlyChosenInactiveOrMissingMachineOrVendor()
    {
        var s = Create();

        Assert.Equal(new[] { "The selected machine is not active." },
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, machineId: 3), 1, null, CancellationToken.None)));
        Assert.Equal(new[] { "The selected supplier is not active." },
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, vendorId: 3), 1, null, CancellationToken.None)));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, machineId: 999), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, vendorId: 999), 1, null, CancellationToken.None));
        Assert.Equal(0, s.Parts.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_CanClearAnInactiveMachine()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(2, Edit(s.Parts, 2, machineId: null, vendorId: null), 1, null, CancellationToken.None);

        Assert.Null(dto.MachineId);
        Assert.Null(dto.VendorId);
        Assert.Null(s.Parts.Stored(2).MachineId);
    }

    [Fact]
    public async Task Update_ValidatesLikeCreate_AndRequiresAValidRowVersion()
    {
        var s = Create();

        Assert.Contains("CurrentStock cannot be negative.",
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, current: -1), 1, null, CancellationToken.None)));
        Assert.Contains("CurrentStock is required.",
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, current: null), 1, null, CancellationToken.None)));
        Assert.Contains("RowVersion is required.",
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, rowVersion: " "), 1, null, CancellationToken.None)));
        Assert.Contains("RowVersion is not valid.",
            await ValidationErrors(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, rowVersion: "@@not-base64@@"), 1, null, CancellationToken.None)));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s.Parts, 1), 1, null, CancellationToken.None));
        Assert.Equal(0, s.Parts.UpdateCalls);
        Assert.Equal(12, s.Parts.Stored(1).CurrentStock);
    }

    [Fact]
    public async Task Update_RejectsANameUsedByAnotherActivePart_ButAllowsItsOwn()
    {
        var s = Create();

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, Edit(s.Parts, 1, name: "HEATER BAND"), 1, null, CancellationToken.None));
        var dto = await s.Service.UpdateAsync(1, Edit(s.Parts, 1, name: "HYDRAULIC SEAL KIT"), 1, null, CancellationToken.None);

        Assert.Equal("HYDRAULIC SEAL KIT", dto.SparePartName);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_Conflicts_AndChangesNothing()
    {
        var s = Create();
        var request = Edit(s.Parts, 1, current: 3);
        s.Parts.SimulateConcurrentModification(1); // e.g. a usage transaction reduced the stock meanwhile

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, request, 1, null, CancellationToken.None));

        Assert.Equal("The spare part was modified by another user. Refresh the spare part and try again.", ex.Message);
        Assert.Equal(12, s.Parts.Stored(1).CurrentStock);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_NeverChangesTheCodeOrActiveFlag()
    {
        var s = Create();

        await s.Service.UpdateAsync(3, Edit(s.Parts, 3, current: 7), 1, null, CancellationToken.None);

        var stored = s.Parts.Stored(3);
        Assert.Equal("SPR-0003", stored.SparePartCode);
        Assert.False(stored.IsActive);
        Assert.Equal(7, stored.CurrentStock);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRowAndStock_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.DeactivateAsync(1, 1, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        var stored = s.Parts.Stored(1);
        Assert.False(stored.IsActive);
        Assert.Equal(12, stored.CurrentStock);
        Assert.Equal(FixedClock.Now, stored.UpdatedAt);
        Assert.Equal(3, s.Parts.Count);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("SparePartDeactivated", entry.Action);
        Assert.Contains(entry.Details, d => d is { FieldName: "is_active", OldValue: "True", NewValue: "False" });
    }

    [Fact]
    public async Task Deactivate_AnInactiveOrMissingPart_Fails_AndWritesNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));
        Assert.Equal("The spare part is already inactive.", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        Assert.Equal(0, s.Parts.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenModifiedMeanwhile_Conflicts()
    {
        var s = Create();
        s.Parts.BeforeWrite = id => s.Parts.SimulateConcurrentModification(id);

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Parts.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewPart(), 1, null, CancellationToken.None);

        Assert.Equal("SPR-0004", dto.SparePartCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewPart(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.SparePartId, Edit(s.Parts, created.SparePartId, current: 1), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.SparePartId, 1, null, CancellationToken.None);

        Assert.Equal(1, updated.CurrentStock);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Parts.Stored(created.SparePartId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
