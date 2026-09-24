using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the vendor tests (service level and HTTP level).</summary>
internal static class VendorTestData
{
    // 1 Chennai Hydraulics (active, full contact data), 2 Precision Mold Tech (active), 3 Old Supplier (inactive).
    public static List<Vendor> Vendors() => new()
    {
        new Vendor
        {
            VendorId = 1, VendorCode = "VND-0001", VendorName = "Chennai Hydraulics", Category = "Spares", ContactPerson = "Mohan",
            Mobile = "9884411001", Email = "mohan@chennaihyd.example", Address = "12 Industrial Estate, Chennai", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Vendor
        {
            VendorId = 2, VendorCode = "VND-0002", VendorName = "Precision Mold Tech", Category = "Mold Repair", IsActive = true,
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
        new Vendor
        {
            VendorId = 3, VendorCode = "VND-0003", VendorName = "Old Supplier", IsActive = false,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>
/// Behaves like the real VendorRepository where it matters: detached copies on read, next VND-NNNN code on add,
/// Update/Deactivate write only the columns the real ones write - and only if the row version still matches.
/// </summary>
internal sealed class InMemoryVendorRepository : IVendorRepository
{
    private readonly List<Vendor> _vendors;
    private int _nextCode;

    public InMemoryVendorRepository(List<Vendor> vendors)
    {
        _vendors = vendors;
        _nextCode = vendors.Count + 1;
    }

    /// <summary>Runs at the start of Update/Deactivate - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeWrite { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int DeactivateCalls { get; private set; }
    public int Count => _vendors.Count;

    public Vendor Stored(int id) => _vendors.Single(v => v.VendorId == id);
    public void SimulateConcurrentModification(int id) => Stored(id).RowVersion = Next(Stored(id).RowVersion);

    private static Vendor Clone(Vendor v) => new()
    {
        VendorId = v.VendorId, VendorCode = v.VendorCode, VendorName = v.VendorName, Category = v.Category, ContactPerson = v.ContactPerson,
        Mobile = v.Mobile, Email = v.Email, Address = v.Address, IsActive = v.IsActive, CreatedAt = v.CreatedAt, CreatedBy = v.CreatedBy,
        UpdatedAt = v.UpdatedAt, UpdatedBy = v.UpdatedBy, RowVersion = (byte[])v.RowVersion.Clone(),
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<Vendor> Items, int TotalCount)> GetAllAsync(VendorListQuery query, CancellationToken cancellationToken)
    {
        IEnumerable<Vendor> q = _vendors;
        if (query.IsActive is { } active) q = q.Where(v => v.IsActive == active);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(v => v.VendorCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                             || v.VendorName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var all = q.OrderBy(v => v.VendorName).ThenBy(v => v.VendorId).ToList();
        var page = all.Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<Vendor>, int)>((page, all.Count));
    }

    public Task<Vendor?> GetByIdAsync(int vendorId, CancellationToken cancellationToken)
    {
        var stored = _vendors.FirstOrDefault(v => v.VendorId == vendorId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<bool> ExistsActiveByNameAsync(string vendorName, int? excludeVendorId, CancellationToken cancellationToken) =>
        Task.FromResult(_vendors.Any(v =>
            v.IsActive && v.VendorId != excludeVendorId && string.Equals(v.VendorName, vendorName, StringComparison.OrdinalIgnoreCase)));

    public Task<Vendor> AddAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        AddCalls++;
        vendor.VendorId = _vendors.Max(v => v.VendorId) + 1;
        vendor.VendorCode = $"VND-{_nextCode++:0000}"; // the real repository issues this from the VENDOR document sequence
        vendor.RowVersion = new byte[] { 1 };
        _vendors.Add(Clone(vendor));
        return Task.FromResult(vendor);
    }

    public Task<Vendor> UpdateAsync(Vendor vendor, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeWrite?.Invoke(vendor.VendorId);

        var stored = _vendors.FirstOrDefault(v => v.VendorId == vendor.VendorId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The vendor was modified by another user. Refresh the vendor and try again.");
        }

        // Exactly the columns the real UpdateAsync writes - never VendorCode, IsActive, creation columns.
        stored.VendorName = vendor.VendorName;
        stored.Category = vendor.Category;
        stored.ContactPerson = vendor.ContactPerson;
        stored.Mobile = vendor.Mobile;
        stored.Email = vendor.Email;
        stored.Address = vendor.Address;
        stored.UpdatedAt = vendor.UpdatedAt;
        stored.UpdatedBy = vendor.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        vendor.RowVersion = stored.RowVersion;
        return Task.FromResult(vendor);
    }

    public Task<Vendor> DeactivateAsync(Vendor vendor, CancellationToken cancellationToken)
    {
        DeactivateCalls++;
        BeforeWrite?.Invoke(vendor.VendorId);

        var stored = _vendors.FirstOrDefault(v => v.VendorId == vendor.VendorId);
        if (stored is null || !stored.RowVersion.SequenceEqual(vendor.RowVersion))
        {
            throw new ConflictException("The vendor was modified by another user. Refresh the vendor and try again.");
        }

        stored.IsActive = false;
        stored.UpdatedAt = vendor.UpdatedAt;
        stored.UpdatedBy = vendor.UpdatedBy;
        stored.RowVersion = Next(stored.RowVersion);
        vendor.RowVersion = stored.RowVersion;
        return Task.FromResult(vendor);
    }
}
