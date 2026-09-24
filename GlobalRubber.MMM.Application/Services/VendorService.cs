using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Vendor master. Rules are exactly those the analysis (section 4.6 and the validation table) and the database define:
/// name required; column lengths; name unique among ACTIVE vendors (the all-masters rule, applied the same way as
/// Department - Q-27 is still open). Category, contact person, mobile, email and address are optional free text with
/// no format checks (none are specified).
/// </summary>
public sealed class VendorService : IVendorService
{
    private const string VendorModule = "Vendor"; // audit "Module" value: the menu name, as for Department/Employee
    private const int NameMaxLength = 150;        // masters.vendor_master.vendor_name NVARCHAR(150)
    private const int CategoryMaxLength = 100;    // category NVARCHAR(100)
    private const int ContactPersonMaxLength = 100; // contact_person NVARCHAR(100)
    private const int MobileMaxLength = 15;       // mobile VARCHAR(15)
    private const int EmailMaxLength = 150;       // email VARCHAR(150)
    private const int AddressMaxLength = 500;     // address NVARCHAR(500)

    private readonly IVendorRepository _vendorRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<VendorService> _logger;

    public VendorService(
        IVendorRepository vendorRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<VendorService> logger)
    {
        _vendorRepository = vendorRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<VendorDto>> GetAllAsync(VendorListQuery query, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _vendorRepository.GetAllAsync(query, cancellationToken);

        return PagedResult<VendorDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<VendorDto> GetByIdAsync(int vendorId, CancellationToken cancellationToken)
    {
        var vendor = await _vendorRepository.GetByIdAsync(vendorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vendor), vendorId);

        return MapToDto(vendor);
    }

    public async Task<VendorDto> CreateAsync(
        CreateVendorRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var fields = NormalizeAndValidate(request, rowVersion: null, requireRowVersion: false, out _);

        await EnsureNameIsFreeAsync(fields.Name, excludeVendorId: null, cancellationToken);

        var vendor = new Vendor
        {
            // VendorId / VendorCode are never client-controlled: the code is issued by the repository from the VENDOR
            // document sequence inside the insert's transaction.
            VendorName = fields.Name,
            Category = fields.Category,
            ContactPerson = fields.ContactPerson,
            Mobile = fields.Mobile,
            Email = fields.Email,
            Address = fields.Address,
            IsActive = true, // a new vendor is always active - see CreateVendorRequest
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _vendorRepository.AddAsync(vendor, cancellationToken);

        await WriteAuditAsync(
            "VendorCreated", created, actingUserId, ipAddress,
            actor => $"{actor} created vendor '{created.VendorName}' ({created.VendorCode}).",
            details: null, cancellationToken);

        return MapToDto(created);
    }

    public async Task<VendorDto> UpdateAsync(
        int vendorId, UpdateVendorRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var vendor = await _vendorRepository.GetByIdAsync(vendorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vendor), vendorId);

        var fields = NormalizeAndValidate(request, request.RowVersion, requireRowVersion: true, out var originalRowVersion);

        await EnsureNameIsFreeAsync(fields.Name, vendorId, cancellationToken);

        // Changed field names in the description; old/new values only for name and category. Contact person, mobile,
        // email and address identify/contact a person and are listed by name only (the Employee/User convention).
        var changes = new List<string>();
        var details = new List<AuditLogDetailEntry>();
        void Track(string field, string? oldValue, string? newValue, bool recordValues)
        {
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
            changes.Add(field);
            if (recordValues) details.Add(new AuditLogDetailEntry(field, oldValue, newValue));
        }

        Track("vendor_name", vendor.VendorName, fields.Name, recordValues: true);
        Track("category", vendor.Category, fields.Category, recordValues: true);
        Track("contact_person", vendor.ContactPerson, fields.ContactPerson, recordValues: false);
        Track("mobile", vendor.Mobile, fields.Mobile, recordValues: false);
        Track("email", vendor.Email, fields.Email, recordValues: false);
        Track("address", vendor.Address, fields.Address, recordValues: false);

        vendor.VendorName = fields.Name;
        vendor.Category = fields.Category;
        vendor.ContactPerson = fields.ContactPerson;
        vendor.Mobile = fields.Mobile;
        vendor.Email = fields.Email;
        vendor.Address = fields.Address;
        vendor.UpdatedAt = _dateTimeProvider.UtcNow;
        vendor.UpdatedBy = actingUserId;

        var updated = await _vendorRepository.UpdateAsync(vendor, originalRowVersion!, cancellationToken);

        var changeSummary = changes.Count > 0 ? $"Changed: {string.Join(", ", changes)}." : "No field values changed.";

        await WriteAuditAsync(
            "VendorUpdated", updated, actingUserId, ipAddress,
            actor => $"{actor} updated vendor '{updated.VendorName}' ({updated.VendorCode}). {changeSummary}",
            details, cancellationToken);

        return MapToDto(updated);
    }

    public async Task<VendorDto> DeactivateAsync(
        int vendorId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var vendor = await _vendorRepository.GetByIdAsync(vendorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Vendor), vendorId);

        // Explicit rather than a silent no-op (same as Role/Department/Employee): nothing is written or audited.
        if (!vendor.IsActive)
        {
            throw new ConflictException("The vendor is already inactive.");
        }

        vendor.IsActive = false;
        vendor.UpdatedAt = _dateTimeProvider.UtcNow;
        vendor.UpdatedBy = actingUserId;

        var deactivated = await _vendorRepository.DeactivateAsync(vendor, cancellationToken);

        await WriteAuditAsync(
            "VendorDeactivated", deactivated, actingUserId, ipAddress,
            actor => $"{actor} deactivated vendor '{deactivated.VendorName}' ({deactivated.VendorCode}).",
            new[] { new AuditLogDetailEntry("is_active", "True", "False") }, cancellationToken);

        return MapToDto(deactivated);
    }

    private async Task EnsureNameIsFreeAsync(string name, int? excludeVendorId, CancellationToken cancellationToken)
    {
        if (await _vendorRepository.ExistsActiveByNameAsync(name, excludeVendorId, cancellationToken))
        {
            throw new ConflictException($"A vendor named '{name}' already exists.");
        }
    }

    private sealed record VendorFields(string Name, string? Category, string? ContactPerson, string? Mobile, string? Email, string? Address);

    // Trim everything, blank optional fields become null. One place so Create and Update can never disagree about what
    // a valid vendor looks like; the row version is only checked (and decoded) for Update.
    private static VendorFields NormalizeAndValidate(
        CreateVendorRequest request, string? rowVersion, bool requireRowVersion, out byte[]? originalRowVersion)
    {
        static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        var fields = new VendorFields(
            (request.VendorName ?? string.Empty).Trim(),
            Optional(request.Category),
            Optional(request.ContactPerson),
            Optional(request.Mobile),
            Optional(request.Email),
            Optional(request.Address));

        var errors = new List<string>();

        if (fields.Name.Length == 0) errors.Add("VendorName is required.");
        else if (fields.Name.Length > NameMaxLength) errors.Add($"VendorName must be at most {NameMaxLength} characters.");

        void MaxLength(string? value, int max, string field)
        {
            if (value is not null && value.Length > max) errors.Add($"{field} must be at most {max} characters.");
        }

        MaxLength(fields.Category, CategoryMaxLength, "Category");
        MaxLength(fields.ContactPerson, ContactPersonMaxLength, "ContactPerson");
        MaxLength(fields.Mobile, MobileMaxLength, "Mobile");
        MaxLength(fields.Email, EmailMaxLength, "Email");
        MaxLength(fields.Address, AddressMaxLength, "Address");

        originalRowVersion = null;
        if (requireRowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion)) errors.Add("RowVersion is required.");
            else if (!TryDecodeRowVersion(rowVersion, out originalRowVersion)) errors.Add("RowVersion is not valid.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return fields;
    }

    private static bool TryDecodeRowVersion(string value, out byte[]? bytes)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    // Everything after the business write has succeeded is audit bookkeeping and must never turn that success into a
    // failure - AuditLogService swallows its own persistence errors and the user-name lookup is guarded.
    private async Task WriteAuditAsync(
        string action, Vendor vendor, int? actingUserId, string? ipAddress,
        Func<string, string> describe, IReadOnlyList<AuditLogDetailEntry>? details, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = VendorModule,
            Action = action,
            EntityName = "Vendor",
            EntityId = vendor.VendorId,
            RecordRef = vendor.VendorCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
            Details = details ?? Array.Empty<AuditLogDetailEntry>(),
        }, cancellationToken);
    }

    private static VendorDto MapToDto(Vendor vendor) => new()
    {
        VendorId = vendor.VendorId,
        VendorCode = vendor.VendorCode,
        VendorName = vendor.VendorName,
        Category = vendor.Category,
        ContactPerson = vendor.ContactPerson,
        Mobile = vendor.Mobile,
        Email = vendor.Email,
        Address = vendor.Address,
        IsActive = vendor.IsActive,
        CreatedAt = vendor.CreatedAt,
        CreatedBy = vendor.CreatedBy,
        UpdatedAt = vendor.UpdatedAt,
        UpdatedBy = vendor.UpdatedBy,
        RowVersion = Convert.ToBase64String(vendor.RowVersion),
    };
}
