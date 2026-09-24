using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public UserRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderBy(u => u.UserName);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

    public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.LoginId == loginId, cancellationToken);

    public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken) =>
        _dbContext.Users
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastLoginAt, lastLoginAtUtc), cancellationToken);

    public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.RoleId == roleId && u.IsActive, cancellationToken);

    public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .AnyAsync(u => u.LoginId == loginId && u.UserId != excludeUserId, cancellationToken);

    public async Task<User> AddAsync(User user, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run
        // inside the execution strategy. If the caller already has a transaction open (a test/verification
        // harness), join it instead of starting another.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(user).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                user.UserCode = await _documentSequence.NextCodeAsync(DocumentTypes.User, cancellationToken);
                _dbContext.Users.Add(user);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(user).State = EntityState.Detached;

            // The transaction (if we own it) rolled back with the sequence increment. Login ID is the only
            // client-supplied unique value (user_code comes from the sequence).
            throw new ConflictException($"A user with login ID '{user.LoginId}' already exists.");
        }

        return user;
    }

    public async Task<User> UpdateAsync(
        User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes) rather than
        // the loaded entity: that keeps the Role navigation out of it, and the row version the CALLER holds
        // becomes the ORIGINAL value EF puts in the UPDATE's WHERE clause - the optimistic-concurrency check,
        // using the existing RowVersion configuration.
        var stub = new User
        {
            UserId = user.UserId,
            RowVersion = originalRowVersion,
            LoginId = user.LoginId,
            UserName = user.UserName,
            Email = user.Email,
            Mobile = user.Mobile,
            RoleId = user.RoleId,
            IsActive = user.IsActive,
            UpdatedAt = user.UpdatedAt,
            UpdatedBy = user.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(u => u.LoginId).IsModified = true;
        entry.Property(u => u.UserName).IsModified = true;
        entry.Property(u => u.Email).IsModified = true;
        entry.Property(u => u.Mobile).IsModified = true;
        entry.Property(u => u.RoleId).IsModified = true;
        entry.Property(u => u.IsActive).IsModified = true;
        entry.Property(u => u.UpdatedAt).IsModified = true;
        entry.Property(u => u.UpdatedBy).IsModified = true;

        if (passwordChanged)
        {
            stub.PasswordHash = user.PasswordHash;
            stub.MustChangePassword = user.MustChangePassword;
            entry.Property(u => u.PasswordHash).IsModified = true;
            entry.Property(u => u.MustChangePassword).IsModified = true;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;

            throw new ConflictException("The user was modified by another user. Refresh the user and try again.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            entry.State = EntityState.Detached;

            throw new ConflictException($"A user with login ID '{user.LoginId}' already exists.");
        }

        user.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        return user;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
