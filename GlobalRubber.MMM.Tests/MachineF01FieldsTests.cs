using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// F01 "List of Machines 2026" fields on the Machine master (migration 022): Range (machine_range), Ownership and
/// Purchase Date - all optional, stored as entered (trimmed), separate from the Installation Date, audited on change,
/// and never touching any other machine field. The real MachineService against the in-memory fakes.
/// </summary>
public class MachineF01FieldsTests
{
    private sealed record Sut(MachineService Service, InMemoryMachineRepository Machines, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var audit = new RecordingAuditLog();
        return new Sut(new MachineService(machines, departments, users, new FixedClock(), audit, NullLogger<MachineService>.Instance), machines, audit);
    }

    private static CreateMachineRequest New(string? range = "40/40 Inches", string? ownership = "GRP", DateOnly? purchase = null) => new()
    {
        MachineName = "Hydraulic Moulding", MachineType = "Hydraulic Moulding", DepartmentId = 2, Location = "Shop Floor 2",
        Manufacturer = "Technocrat", Capacity = "450 Ton", InstallationDate = new DateOnly(2009, 2, 1), MaintenanceFrequencyDays = 30,
        Criticality = "High", MachineRange = range, Ownership = ownership, PurchaseDate = purchase ?? new DateOnly(2008, 6, 1),
    };

    private static UpdateMachineRequest Edit(Sut s, int id, string? range, string? ownership, DateOnly? purchase)
    {
        var m = s.Machines.Stored(id);
        return new UpdateMachineRequest
        {
            MachineName = m.MachineName, MachineType = m.MachineType, DepartmentId = m.DepartmentId, Location = m.Location,
            Manufacturer = m.Manufacturer, Model = m.Model, SerialNumber = m.SerialNumber, Capacity = m.Capacity,
            InstallationDate = m.InstallationDate, MaintenanceFrequencyDays = m.MaintenanceFrequencyDays, Criticality = m.Criticality,
            Remarks = m.Remarks, MachineRange = range, Ownership = ownership, PurchaseDate = purchase,
            RowVersion = Convert.ToBase64String(m.RowVersion),
        };
    }

    [Fact]
    public async Task Create_StoresRangeOwnershipAndPurchaseDate_SeparatelyFromTheInstallationDate()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(range: "  40/40 Inches  ", ownership: " GRP "), 1, null, CancellationToken.None);

        Assert.Equal(("40/40 Inches", "GRP", new DateOnly(2008, 6, 1), new DateOnly(2009, 2, 1)),
            (dto.MachineRange, dto.Ownership, dto.PurchaseDate!.Value, dto.InstallationDate!.Value));
        var stored = s.Machines.Stored(dto.MachineId);
        Assert.Equal(("40/40 Inches", "GRP", new DateOnly(2008, 6, 1)), (stored.MachineRange, stored.Ownership, stored.PurchaseDate!.Value));
        Assert.StartsWith("MAC-", dto.MachineCode); // the code is still issued by the MACHINE sequence
    }

    [Fact]
    public async Task Create_WithoutTheF01Fields_StoresNull_NeverAPlaceholder()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(range: "   ", ownership: null), 1, null, CancellationToken.None);
        var noDate = await s.Service.CreateAsync(new CreateMachineRequest
        {
            MachineName = "Strip cutting machine", MachineType = "Cutting", DepartmentId = 2, Location = "Shop Floor 2", MaintenanceFrequencyDays = 30, Criticality = "Low",
        }, 1, null, CancellationToken.None);

        Assert.Null(dto.MachineRange);
        Assert.Null(dto.Ownership);
        Assert.Equal((null, null, null), (noDate.MachineRange, noDate.Ownership, noDate.PurchaseDate));
    }

    [Fact]
    public async Task Update_ChangesAndClearsTheF01Fields_AndAuditsEachChange()
    {
        var s = Create();
        var before = s.Machines.Stored(1);
        var untouched = (before.MachineType, before.DepartmentId, before.Location, before.Model, before.SerialNumber,
            before.MaintenanceFrequencyDays, before.Criticality, before.OperationalStatus, before.Remarks, before.IsActive, before.InstallationDate);

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, "32/32 Inches", "DAI", new DateOnly(2023, 1, 15)), 1, null, CancellationToken.None);

        Assert.Equal(("32/32 Inches", "DAI", new DateOnly(2023, 1, 15)), (dto.MachineRange, dto.Ownership, dto.PurchaseDate!.Value));
        var after = s.Machines.Stored(1);
        Assert.Equal(untouched, (after.MachineType, after.DepartmentId, after.Location, after.Model, after.SerialNumber,
            after.MaintenanceFrequencyDays, after.Criticality, after.OperationalStatus, after.Remarks, after.IsActive, after.InstallationDate));
        var details = s.Audit.Entries.Last().Details;
        Assert.Contains(details, d => d is { FieldName: "machine_range", OldValue: null, NewValue: "32/32 Inches" });
        Assert.Contains(details, d => d is { FieldName: "ownership", OldValue: null, NewValue: "DAI" });
        Assert.Contains(details, d => d is { FieldName: "purchase_date", OldValue: null, NewValue: "2023-01-15" });

        var cleared = await s.Service.UpdateAsync(1, Edit(s, 1, null, " ", null), 1, null, CancellationToken.None);
        Assert.Equal((null, null, null), (cleared.MachineRange, cleared.Ownership, cleared.PurchaseDate));
    }

    [Fact]
    public async Task GetById_AndList_ReturnTheF01Fields_AndNullForExistingMachines()
    {
        var s = Create();
        var existing = await s.Service.GetByIdAsync(1, CancellationToken.None);
        Assert.Equal((null, null, null), (existing.MachineRange, existing.Ownership, existing.PurchaseDate)); // existing rows stay NULL

        var created = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);
        var listed = (await s.Service.GetAllAsync(new MachineListQuery { PageNumber = 1, PageSize = 50 }, CancellationToken.None)).Items.Single(i => i.MachineId == created.MachineId);
        Assert.Equal(("40/40 Inches", "GRP", new DateOnly(2008, 6, 1)), (listed.MachineRange, listed.Ownership, listed.PurchaseDate!.Value));
    }

    [Fact]
    public async Task RangeAndOwnership_Over100Characters_Are400()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(range: new string('r', 101), ownership: new string('o', 101)), 1, null, CancellationToken.None));

        Assert.Contains("MachineRange must be at most 100 characters.", ex.Errors);
        Assert.Contains("Ownership must be at most 100 characters.", ex.Errors);
        Assert.Equal(100, (await s.Service.CreateAsync(New(range: new string('r', 100), ownership: new string('o', 100)), 1, null, CancellationToken.None)).MachineRange!.Length);
    }

    [Fact]
    public async Task TheExistingRulesStillApply_AndTheCodeStaysServerIssued()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateMachineRequest
        {
            MachineRange = "40/40 Inches", Ownership = "GRP", PurchaseDate = new DateOnly(2008, 1, 1), // the F01 fields alone are not a machine
        }, 1, null, CancellationToken.None));
        Assert.Contains("MachineName is required.", missing.Errors);
        Assert.Contains("MachineType is required.", missing.Errors);
        Assert.Contains("Location is required.", missing.Errors);

        var a = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);
        var b = await s.Service.CreateAsync(new CreateMachineRequest
        {
            MachineName = "Hydraulic Moulding 2", MachineType = "Hydraulic Moulding", DepartmentId = 2, Location = "Shop Floor 2", MaintenanceFrequencyDays = 30, Criticality = "High",
        }, 1, null, CancellationToken.None);
        Assert.NotEqual(a.MachineCode, b.MachineCode); // codes come from the sequence - never duplicated, never client-chosen
    }
}
