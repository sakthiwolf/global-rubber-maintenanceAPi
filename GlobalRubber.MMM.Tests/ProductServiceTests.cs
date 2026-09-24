using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real ProductService against in-memory fakes. Acting user 1 "Sakthi". Products: 1 Rubber Seal A, 2 Gasket Set D
/// (active), 3 Old Product (inactive).
/// </summary>
public class ProductServiceTests
{
    private sealed record Sut(ProductService Service, InMemoryProductRepository Products, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var audit = new RecordingAuditLog();
        var service = new ProductService(products, users, new FixedClock(), auditOverride ?? audit, NullLogger<ProductService>.Instance);
        return new Sut(service, products, audit, users);
    }

    private static CreateProductRequest NewProduct(string name = "O-Ring 45mm", string uom = "PCS", decimal? cycle = 20m) =>
        new() { ProductName = name, Category = "O-Rings", UnitOfMeasure = uom, StandardCycleTimeSec = cycle, Remarks = "New mold" };

    private static string Rv(InMemoryProductRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request defaulting to "no change", so each test states only what it changes.
    private static UpdateProductRequest Edit(
        InMemoryProductRepository repo, int id, string? name = null, string? category = "keep", string? uom = null,
        decimal? cycle = -1m, string? remarks = "keep", string? rowVersion = null)
    {
        var s = repo.Stored(id);
        return new UpdateProductRequest
        {
            ProductName = name ?? s.ProductName,
            Category = category == "keep" ? s.Category : category,
            UnitOfMeasure = uom ?? s.UnitOfMeasure,
            StandardCycleTimeSec = cycle == -1m ? s.StandardCycleTimeSec : cycle,
            Remarks = remarks == "keep" ? s.Remarks : remarks,
            RowVersion = rowVersion ?? Rv(repo, id),
        };
    }

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName()
    {
        var result = await Create().Service.GetAllAsync(new ProductListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Gasket Set D", "Old Product" }, result.Items.Select(i => i.ProductName));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AndSearchesCodeAndName()
    {
        var s = Create();

        Assert.Equal(2, (await s.Service.GetAllAsync(new ProductListQuery { IsActive = true }, CancellationToken.None)).TotalCount);
        Assert.Equal("Old Product", Assert.Single((await s.Service.GetAllAsync(new ProductListQuery { IsActive = false }, CancellationToken.None)).Items).ProductName);
        Assert.Equal("Rubber Seal A", Assert.Single((await s.Service.GetAllAsync(new ProductListQuery { Search = "seal" }, CancellationToken.None)).Items).ProductName);
        Assert.Equal("Old Product", Assert.Single((await s.Service.GetAllAsync(new ProductListQuery { Search = "prd-0003" }, CancellationToken.None)).Items).ProductName);
    }

    [Fact]
    public async Task GetById_ReturnsEveryField()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("PRD-0001", dto.ProductCode);
        Assert.Equal("Rubber Seal A", dto.ProductName);
        Assert.Equal("Seals", dto.Category);
        Assert.Equal("PCS", dto.UnitOfMeasure);
        Assert.Equal(45m, dto.StandardCycleTimeSec);
        Assert.Equal("Line 1", dto.Remarks);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
    }

    [Fact]
    public async Task GetById_ThrowsNotFound_ForAnUnknownProduct()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_TrimsFields_SetsActiveAndCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(
            new CreateProductRequest { ProductName = "  O-Ring 45mm ", Category = "  ", UnitOfMeasure = " PCS ", StandardCycleTimeSec = 20.5m, Remarks = " r " },
            1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("PRD-0004", dto.ProductCode);
        Assert.Equal("O-Ring 45mm", dto.ProductName);
        Assert.Null(dto.Category);
        Assert.Equal("PCS", dto.UnitOfMeasure);
        Assert.Equal(20.5m, dto.StandardCycleTimeSec);
        Assert.Equal("r", dto.Remarks);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Null(dto.UpdatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ProductCreated", entry.Action);
        Assert.Equal("Product", entry.Module);
        Assert.Equal("Product", entry.EntityName);
        Assert.Equal(dto.ProductId, entry.EntityId);
        Assert.Equal("PRD-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Create_WithoutACycleTime_IsAllowed()
    {
        var dto = await Create().Service.CreateAsync(NewProduct(cycle: null), 1, null, CancellationToken.None);

        Assert.Null(dto.StandardCycleTimeSec);
    }

    [Fact]
    public async Task Create_RequiresNameAndUnitOfMeasure_AndEnforcesEveryColumnLength_WritingNothing()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(new CreateProductRequest { ProductName = " ", UnitOfMeasure = "" }, 1, null, CancellationToken.None));
        Assert.Contains("ProductName is required.", missing.Errors);
        Assert.Contains("UnitOfMeasure is required.", missing.Errors);

        var tooLong = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateProductRequest
        {
            ProductName = new string('n', 151), Category = new string('c', 101), UnitOfMeasure = new string('u', 21), Remarks = new string('r', 501),
        }, 1, null, CancellationToken.None));
        Assert.Contains("ProductName must be at most 150 characters.", tooLong.Errors);
        Assert.Contains("Category must be at most 100 characters.", tooLong.Errors);
        Assert.Contains("UnitOfMeasure must be at most 20 characters.", tooLong.Errors);
        Assert.Contains("Remarks must be at most 500 characters.", tooLong.Errors);

        Assert.Equal(0, s.Products.AddCalls);
        Assert.Empty(s.Audit.Entries);

        var atLimit = await s.Service.CreateAsync(new CreateProductRequest
        {
            ProductName = new string('n', 150), Category = new string('c', 100), UnitOfMeasure = new string('u', 20), Remarks = new string('r', 500),
        }, 1, null, CancellationToken.None);
        Assert.Equal(150, atLimit.ProductName.Length);
    }

    [Theory]
    [InlineData("0", "StandardCycleTimeSec must be greater than 0.")]
    [InlineData("-5", "StandardCycleTimeSec must be greater than 0.")]
    [InlineData("1000000", "StandardCycleTimeSec must be at most 999999.99.")]
    [InlineData("12.345", "StandardCycleTimeSec can have at most 2 decimal places.")]
    public async Task Create_EnforcesTheCycleTimeCheckConstraintAndDecimalShape(string value, string expectedError)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(NewProduct(cycle: decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)), 1, null, CancellationToken.None));

        Assert.Contains(expectedError, ex.Errors);
        Assert.Equal(0, s.Products.AddCalls);
    }

    [Theory]
    [InlineData("Rubber Seal A")]
    [InlineData("  rubber seal a ")]
    public async Task Create_RefusesADuplicateActiveName_CaseInsensitively_WithConflict(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewProduct(name), 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(0, s.Products.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveProduct()
    {
        var dto = await Create().Service.CreateAsync(NewProduct("Old Product"), 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesTheEditableFields_OnlyThoseColumns_AndSetsUpdatedBy()
    {
        var s = Create();
        var before = s.Products.Stored(1);
        var (code, active, createdBy, createdAt, oldVersion) = (before.ProductCode, before.IsActive, before.CreatedBy, before.CreatedAt, Rv(s.Products, 1));

        var dto = await s.Service.UpdateAsync(1, Edit(s.Products, 1, name: "Rubber Seal A2", category: null, uom: "SET", cycle: null, remarks: "Line 2"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Rubber Seal A2", dto.ProductName);
        Assert.Null(dto.Category);
        Assert.Equal("SET", dto.UnitOfMeasure);
        Assert.Null(dto.StandardCycleTimeSec);
        Assert.Equal("Line 2", dto.Remarks);
        var after = s.Products.Stored(1);
        Assert.Equal((code, active, createdBy, createdAt), (after.ProductCode, after.IsActive, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);
    }

    [Fact]
    public async Task Update_AuditsFieldLevelChanges_WithOldAndNewValues()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Products, 1, uom: "SET", cycle: 50.25m), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ProductUpdated", entry.Action);
        Assert.Equal("PRD-0001", entry.RecordRef);
        Assert.Contains("Changed: unit_of_measure, standard_cycle_time_sec.", entry.Description);
        Assert.Equal(2, entry.Details.Count);
        Assert.Contains(entry.Details, d => d is { FieldName: "unit_of_measure", OldValue: "PCS", NewValue: "SET" });
        Assert.Contains(entry.Details, d => d is { FieldName: "standard_cycle_time_sec", OldValue: "45.00", NewValue: "50.25" });
    }

    [Fact]
    public async Task Update_WithNoChange_StillSucceeds_AndSaysNothingChanged()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Products, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains("No field values changed.", entry.Description);
        Assert.Empty(entry.Details);
    }

    [Fact]
    public async Task Update_AcceptsItsOwnName_ButRefusesAnotherActiveProductsName()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Products, 1, name: "RUBBER SEAL A"), 1, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Products, 1, name: "gasket set d"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("RUBBER SEAL A", s.Products.Stored(1).ProductName);
        Assert.Single(s.Audit.Entries); // only the first, successful update
    }

    [Fact]
    public async Task Update_ThrowsNotFound_ForAnUnknownProduct()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Service.UpdateAsync(999, new UpdateProductRequest { ProductName = "X", UnitOfMeasure = "PCS", RowVersion = "AQ==" }, 1, null, CancellationToken.None));

        Assert.Equal(0, s.Products.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ValidatesFields_AndTheRowVersion()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, new UpdateProductRequest { ProductName = " ", UnitOfMeasure = " ", StandardCycleTimeSec = 0m, RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("ProductName is required.", ex.Errors);
        Assert.Contains("UnitOfMeasure is required.", ex.Errors);
        Assert.Contains("StandardCycleTimeSec must be greater than 0.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Products, 1, rowVersion: "not base64 !!"), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Products.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var stale = Edit(s.Products, 1, name: "Stale edit");
        s.Products.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Rubber Seal A", s.Products.Stored(1).ProductName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRow_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Products.Stored(1);
        var snapshot = (before.ProductCode, before.ProductName, before.Category, before.UnitOfMeasure, before.StandardCycleTimeSec, before.Remarks, before.CreatedBy, before.CreatedAt);

        var dto = await s.Service.DeactivateAsync(1, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Products.Stored(1);
        Assert.False(after.IsActive);
        Assert.Equal(snapshot, (after.ProductCode, after.ProductName, after.Category, after.UnitOfMeasure, after.StandardCycleTimeSec, after.Remarks, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(3, s.Products.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ProductDeactivated", entry.Action);
        Assert.Equal("PRD-0001", entry.RecordRef);
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
        Assert.Equal(0, s.Products.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Products.BeforeWrite = s.Products.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Products.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewProduct(), 1, null, CancellationToken.None);

        Assert.Equal("PRD-0004", dto.ProductCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewProduct(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.ProductId, Edit(s.Products, created.ProductId, name: "O-Ring 50mm"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.ProductId, 1, null, CancellationToken.None);

        Assert.Equal("O-Ring 50mm", updated.ProductName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Products.Stored(created.ProductId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
