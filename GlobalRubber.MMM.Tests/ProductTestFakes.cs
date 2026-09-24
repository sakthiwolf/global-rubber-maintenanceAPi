using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the product tests (service level and HTTP level).</summary>
internal static class ProductTestData
{
    // 1 Rubber Seal A (active, full data), 2 Gasket Set D (active), 3 Old Product (inactive).
    public static List<Product> Products() => new()
    {
        new Product
        {
            ProductId = 1, ProductCode = "PRD-0001", ProductName = "Rubber Seal A", Category = "Seals", UnitOfMeasure = "PCS",
            StandardCycleTimeSec = 45m, Remarks = "Line 1", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Product
        {
            ProductId = 2, ProductCode = "PRD-0002", ProductName = "Gasket Set D", Category = "Gaskets", UnitOfMeasure = "SET", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Product
        {
            ProductId = 3, ProductCode = "PRD-0003", ProductName = "Old Product", UnitOfMeasure = "PCS", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real ProductRepository where it matters: detached copies on read, next PRD-NNNN code on add,
/// Update/Deactivate write only the columns the real ones write - and only if the row version still matches.
/// </summary>
internal sealed class InMemoryProductRepository : IProductRepository
{
    private readonly List<Product> _products;
    private int _nextCode;

    public InMemoryProductRepository(List<Product> products)
    {
        _products = products;
        _nextCode = products.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _products.Count;

    public Product Stored(int id) => _products.Single(p => p.ProductId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static Product Clone(Product p) => new()
    {
        ProductId = p.ProductId, ProductCode = p.ProductCode, ProductName = p.ProductName, Category = p.Category,
        UnitOfMeasure = p.UnitOfMeasure, StandardCycleTimeSec = p.StandardCycleTimeSec, Remarks = p.Remarks, IsActive = p.IsActive,
        CreatedAt = p.CreatedAt, CreatedBy = p.CreatedBy, UpdatedAt = p.UpdatedAt, UpdatedBy = p.UpdatedBy,
        RowVersion = (byte[])p.RowVersion.Clone(),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Product> Items, int TotalCount)> GetAllAsync(ProductListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Product> q = _products;
        if (query.IsActive is { } active) q = q.Where(p => p.IsActive == active);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(p => p.ProductCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || p.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(p => p.ProductName).ThenBy(p => p.ProductId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Product>, int)>((page, all.Count));
    }

    public Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken)
    {
        var stored = _products.FirstOrDefault(p => p.ProductId == productId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string productName, int? excludeProductId, CancellationToken cancellationToken) =>
        Task.FromResult(_products.Any(p =>
            p.IsActive && p.ProductId != excludeProductId && string.Equals(p.ProductName, productName, StringComparison.OrdinalIgnoreCase)));

    public Task<Product> AddAsync(Product product, CancellationToken cancellationToken)
    {
        AddCalls++;
        product.ProductId = _products.Max(p => p.ProductId) + 1;
        product.ProductCode = $"PRD-{_nextCode++:0000}"; // the real repository issues this from the PRODUCT document sequence
        product.RowVersion = new byte[] { 1 };
        _products.Add(Clone(product));
        return Task.FromResult(product);
    }

    public Task<Product> UpdateAsync(Product product, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(product.ProductId);

        var stored = _products.FirstOrDefault(p => p.ProductId == product.ProductId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The product was modified by another user. Refresh the product and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never ProductCode, IsActive, creation columns.
        stored.ProductName = product.ProductName;
        stored.Category = product.Category;
        stored.UnitOfMeasure = product.UnitOfMeasure;
        stored.StandardCycleTimeSec = product.StandardCycleTimeSec;
        stored.Remarks = product.Remarks;
        stored.UpdatedAt = product.UpdatedAt;
        stored.UpdatedBy = product.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        product.RowVersion = stored.RowVersion;
        return Task.FromResult(product);
    }

    public Task<Product> DeactivateAsync(Product product, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(product.ProductId);

        var stored = _products.FirstOrDefault(p => p.ProductId == product.ProductId);
        if (stored is null || !stored.RowVersion.SequenceEqual(product.RowVersion))
        {
            throw new ConflictException("The product was modified by another user. Refresh the product and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = product.UpdatedAt;
        stored.UpdatedBy = product.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        product.RowVersion = stored.RowVersion;
        return Task.FromResult(product);
    }
}
