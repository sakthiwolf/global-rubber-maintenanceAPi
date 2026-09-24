using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real VendorService against in-memory fakes. Acting user 1 "Sakthi". Vendors: 1 Chennai Hydraulics, 2 Precision
/// Mold Tech (active), 3 Old Supplier (inactive).
/// </summary>
public class VendorServiceTests
{
    private sealed record Sut(VendorService Service, InMemoryVendorRepository Vendors, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var audit = new RecordingAuditLog();
        var service = new VendorService(vendors, users, new FixedClock(), auditOverride ?? audit, NullLogger<VendorService>.Instance);
        return new Sut(service, vendors, audit, users);
    }

    private static CreateVendorRequest NewVendor(string name = "SKF Bearings Distributor", string? category = "Spares") =>
        new() { VendorName = name, Category = category, ContactPerson = "Vignesh", Mobile = "9884411003", Email = "sales@skf.example", Address = "Guindy, Chennai" };

    private static string Rv(InMemoryVendorRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request defaulting to "no change", so each test states only what it changes.
    private static UpdateVendorRequest Edit(
        InMemoryVendorRepository repo, int id, string? name = null, string? category = "keep", string? contactPerson = "keep",
        string? mobile = "keep", string? email = "keep", string? address = "keep", string? rowVersion = null)
    {
        var s = repo.Stored(id);
        string? Keep(string? requested, string? current) => requested == "keep" ? current : requested;
        return new UpdateVendorRequest
        {
            VendorName = name ?? s.VendorName,
            Category = Keep(category, s.Category),
            ContactPerson = Keep(contactPerson, s.ContactPerson),
            Mobile = Keep(mobile, s.Mobile),
            Email = Keep(email, s.Email),
            Address = Keep(address, s.Address),
            RowVersion = rowVersion ?? Rv(repo, id),
        };
    }

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName()
    {
        var result = await Create().Service.GetAllAsync(new VendorListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Chennai Hydraulics", "Old Supplier" }, result.Items.Select(i => i.VendorName));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AndSearchesCodeAndName()
    {
        var s = Create();

        Assert.Equal(2, (await s.Service.GetAllAsync(new VendorListQuery { IsActive = true }, CancellationToken.None)).TotalCount);
        Assert.Equal("Old Supplier", Assert.Single((await s.Service.GetAllAsync(new VendorListQuery { IsActive = false }, CancellationToken.None)).Items).VendorName);
        Assert.Equal("Precision Mold Tech", Assert.Single((await s.Service.GetAllAsync(new VendorListQuery { Search = "mold" }, CancellationToken.None)).Items).VendorName);
        Assert.Equal("Old Supplier", Assert.Single((await s.Service.GetAllAsync(new VendorListQuery { Search = "vnd-0003" }, CancellationToken.None)).Items).VendorName);
    }

    [Fact]
    public async Task GetById_ReturnsEveryField()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("VND-0001", dto.VendorCode);
        Assert.Equal("Chennai Hydraulics", dto.VendorName);
        Assert.Equal("Spares", dto.Category);
        Assert.Equal("Mohan", dto.ContactPerson);
        Assert.Equal("9884411001", dto.Mobile);
        Assert.Equal("mohan@chennaihyd.example", dto.Email);
        Assert.Equal("12 Industrial Estate, Chennai", dto.Address);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
    }

    [Fact]
    public async Task GetById_ThrowsNotFound_ForAnUnknownVendor()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_TrimsFields_SetsActiveAndCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(
            new CreateVendorRequest { VendorName = "  SKF Bearings  ", Category = " Spares ", ContactPerson = "  ", Mobile = " 9884411003 ", Email = "", Address = " Guindy " },
            1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("VND-0004", dto.VendorCode);
        Assert.Equal("SKF Bearings", dto.VendorName);
        Assert.Equal("Spares", dto.Category);
        Assert.Null(dto.ContactPerson);
        Assert.Equal("9884411003", dto.Mobile);
        Assert.Null(dto.Email);
        Assert.Equal("Guindy", dto.Address);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Null(dto.UpdatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("VendorCreated", entry.Action);
        Assert.Equal("Vendor", entry.Module);
        Assert.Equal("Vendor", entry.EntityName);
        Assert.Equal(dto.VendorId, entry.EntityId);
        Assert.Equal("VND-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("SKF Bearings", entry.Description);
    }

    [Fact]
    public async Task Create_RequiresTheName_AndEnforcesEveryColumnLength_WritingNothing()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(new CreateVendorRequest { VendorName = "   " }, 1, null, CancellationToken.None));
        Assert.Contains("VendorName is required.", missing.Errors);

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateVendorRequest
        {
            VendorName = new string('n', 151), Category = new string('c', 101), ContactPerson = new string('p', 101),
            Mobile = new string('9', 16), Email = new string('e', 151), Address = new string('a', 501),
        }, 1, null, CancellationToken.None));
        Assert.Contains("VendorName must be at most 150 characters.", tooLong.Errors);
        Assert.Contains("Category must be at most 100 characters.", tooLong.Errors);
        Assert.Contains("ContactPerson must be at most 100 characters.", tooLong.Errors);
        Assert.Contains("Mobile must be at most 15 characters.", tooLong.Errors);
        Assert.Contains("Email must be at most 150 characters.", tooLong.Errors);
        Assert.Contains("Address must be at most 500 characters.", tooLong.Errors);

        Assert.Equal(0, s.Vendors.AddCalls);
        Assert.Empty(s.Audit.Entries);

        var atLimit = await s.Service.CreateAsync(new CreateVendorRequest
        {
            VendorName = new string('n', 150), Category = new string('c', 100), ContactPerson = new string('p', 100),
            Mobile = new string('9', 15), Email = new string('e', 150), Address = new string('a', 500),
        }, 1, null, CancellationToken.None);
        Assert.Equal(150, atLimit.VendorName.Length);
    }

    [Theory]
    [InlineData("Chennai Hydraulics")]
    [InlineData("  chennai hydraulics ")]
    public async Task Create_RefusesADuplicateActiveName_CaseInsensitively_WithConflict(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewVendor(name), 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(0, s.Vendors.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveVendor()
    {
        var dto = await Create().Service.CreateAsync(NewVendor("Old Supplier"), 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesTheEditableFields_OnlyThoseColumns_AndSetsUpdatedBy()
    {
        var s = Create();
        var before = s.Vendors.Stored(2);
        var (code, active, createdBy, createdAt, oldVersion) = (before.VendorCode, before.IsActive, before.CreatedBy, before.CreatedAt, Rv(s.Vendors, 2));

        var dto = await s.Service.UpdateAsync(2, Edit(s.Vendors, 2, name: "Precision Molds", category: null, contactPerson: "Elango", mobile: "9884411002", email: "e@pmt.example", address: "Ambattur"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Precision Molds", dto.VendorName);
        Assert.Null(dto.Category);
        Assert.Equal("Elango", dto.ContactPerson);
        Assert.Equal("9884411002", dto.Mobile);
        Assert.Equal("e@pmt.example", dto.Email);
        Assert.Equal("Ambattur", dto.Address);
        var after = s.Vendors.Stored(2);
        Assert.Equal((code, active, createdBy, createdAt), (after.VendorCode, after.IsActive, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);
    }

    [Fact]
    public async Task Update_AuditsChangedFieldNames_WithValuesOnlyForNameAndCategory()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Vendors, 1, name: "Chennai Hydraulics Pvt Ltd", category: "Spares & Service", contactPerson: "Ravi", mobile: "9000000000", email: "new@example.com", address: "New Address"), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("VendorUpdated", entry.Action);
        Assert.Equal("VND-0001", entry.RecordRef);
        Assert.Contains("Changed: vendor_name, category, contact_person, mobile, email, address.", entry.Description);
        Assert.Equal(2, entry.Details.Count);
        Assert.Contains(entry.Details, d => d is { FieldName: "vendor_name", OldValue: "Chennai Hydraulics", NewValue: "Chennai Hydraulics Pvt Ltd" });
        Assert.Contains(entry.Details, d => d is { FieldName: "category", OldValue: "Spares", NewValue: "Spares & Service" });
        var everything = System.Text.Json.JsonSerializer.Serialize(entry);
        foreach (var contact in new[] { "Mohan", "9884411001", "mohan@chennaihyd.example", "Industrial Estate", "Ravi", "9000000000", "new@example.com", "New Address" })
        {
            Assert.DoesNotContain(contact, everything);
        }
    }

    [Fact]
    public async Task Update_WithNoChange_StillSucceeds_AndSaysNothingChanged()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Vendors, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains("No field values changed.", entry.Description);
        Assert.Empty(entry.Details);
    }

    [Fact]
    public async Task Update_AcceptsItsOwnName_ButRefusesAnotherActiveVendorsName()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Vendors, 1, name: "CHENNAI HYDRAULICS"), 1, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Vendors, 1, name: "precision mold tech"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("CHENNAI HYDRAULICS", s.Vendors.Stored(1).VendorName);
        Assert.Single(s.Audit.Entries); // only the first, successful update
    }

    [Fact]
    public async Task Update_ThrowsNotFound_ForAnUnknownVendor()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Service.UpdateAsync(999, new UpdateVendorRequest { VendorName = "X", RowVersion = "AQ==" }, 1, null, CancellationToken.None));

        Assert.Equal(0, s.Vendors.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ValidatesFields_AndTheRowVersion()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, new UpdateVendorRequest { VendorName = " ", Address = new string('a', 501), RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("VendorName is required.", ex.Errors);
        Assert.Contains("Address must be at most 500 characters.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Vendors, 1, rowVersion: "not base64 !!"), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Vendors.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var stale = Edit(s.Vendors, 1, name: "Stale edit");
        s.Vendors.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Chennai Hydraulics", s.Vendors.Stored(1).VendorName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRow_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Vendors.Stored(1);
        var snapshot = (before.VendorCode, before.VendorName, before.Category, before.ContactPerson, before.Mobile, before.Email, before.Address, before.CreatedBy, before.CreatedAt);

        var dto = await s.Service.DeactivateAsync(1, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Vendors.Stored(1);
        Assert.False(after.IsActive);
        Assert.Equal(snapshot, (after.VendorCode, after.VendorName, after.Category, after.ContactPerson, after.Mobile, after.Email, after.Address, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(3, s.Vendors.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("VendorDeactivated", entry.Action);
        Assert.Equal("VND-0001", entry.RecordRef);
        Assert.Equal(1, entry.EntityId);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Deactivate_ThrowsNotFound_ForUnknown_AndConflict_ForAlreadyInactive_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Contains("already inactive", ex.Message);
        Assert.Equal(0, s.Vendors.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Vendors.BeforeWrite = s.Vendors.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Vendors.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewVendor(), 1, null, CancellationToken.None);

        Assert.Equal("VND-0004", dto.VendorCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewVendor(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.VendorId, Edit(s.Vendors, created.VendorId, name: "SKF Bearings"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.VendorId, 1, null, CancellationToken.None);

        Assert.Equal("SKF Bearings", updated.VendorName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Vendors.Stored(created.VendorId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
