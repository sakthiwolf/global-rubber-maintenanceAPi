using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

public class AuditLogServiceTests
{
    [Fact]
    public async Task LogAsync_PersistsEntry_WithStampedEventAt_AndMappedFields()
    {
        var repository = new FakeAuditLogRepository();
        var dateTimeProvider = new FixedDateTimeProvider(new DateTime(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc));
        var service = new AuditLogService(repository, dateTimeProvider, NullLogger<AuditLogService>.Instance);

        await service.LogAsync(new AuditLogEntry
        {
            UserId = 7,
            UserName = "Ravi Kumar",
            Module = "Security",
            Action = "Login",
            EntityName = "User",
            EntityId = 7,
            RecordRef = "USR-0007",
            Description = "Ravi Kumar logged in successfully.",
            IpAddress = "10.0.0.5",
        }, CancellationToken.None);

        var saved = Assert.Single(repository.AddedLogs);
        Assert.Equal(dateTimeProvider.UtcNow, saved.EventAt);
        Assert.Equal(7, saved.UserId);
        Assert.Equal("Ravi Kumar", saved.UserName);
        Assert.Equal("Security", saved.Module);
        Assert.Equal("Login", saved.Action);
        Assert.Equal("User", saved.EntityName);
        Assert.Equal(7, saved.EntityId);
        Assert.Equal("USR-0007", saved.RecordRef);
        Assert.Equal("10.0.0.5", saved.IpAddress);
    }

    [Fact]
    public async Task LogAsync_PersistsFieldLevelDetails_WithTheAuditLogRow()
    {
        var repository = new FakeAuditLogRepository();
        var service = new AuditLogService(
            repository, new FixedDateTimeProvider(new DateTime(2026, 5, 1, 9, 30, 0, DateTimeKind.Utc)), NullLogger<AuditLogService>.Instance);

        await service.LogAsync(new AuditLogEntry
        {
            UserName = "Ravi Kumar",
            Module = "Security",
            Action = "PermissionsUpdated",
            Description = "changed",
            Details = new[]
            {
                new AuditLogDetailEntry("ADMIN_USERS.can_add", "false", "true"),
                new AuditLogDetailEntry("ADMIN_USERS.can_edit", "true", "false"),
            },
        }, CancellationToken.None);

        var saved = Assert.Single(repository.AddedLogs);
        Assert.Equal(2, saved.Details.Count);
        var first = saved.Details.First();
        Assert.Equal("ADMIN_USERS.can_add", first.FieldName);
        Assert.Equal("false", first.OldValue);
        Assert.Equal("true", first.NewValue);
    }

    [Fact]
    public async Task LogAsync_WithoutDetails_WritesNoDetailRows()
    {
        var repository = new FakeAuditLogRepository();
        var service = new AuditLogService(
            repository, new FixedDateTimeProvider(DateTime.UtcNow), NullLogger<AuditLogService>.Instance);

        await service.LogAsync(new AuditLogEntry { UserName = "x", Module = "Security", Action = "Login", Description = "d" }, CancellationToken.None);

        Assert.Empty(Assert.Single(repository.AddedLogs).Details);
    }

    [Fact]
    public async Task LogAsync_DoesNotThrow_WhenRepositoryFails()
    {
        // The failure-behavior decision: a logging failure never fails (or surfaces to) the
        // business operation it describes - see AuditLogService's own remarks.
        var service = new AuditLogService(
            new ThrowingAuditLogRepository(), new FixedDateTimeProvider(DateTime.UtcNow), NullLogger<AuditLogService>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            service.LogAsync(
                new AuditLogEntry { UserName = "x", Module = "Security", Action = "Login", Description = "d" },
                CancellationToken.None));

        Assert.Null(exception);
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public FixedDateTimeProvider(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class FakeAuditLogRepository : IAuditLogRepository
    {
        public List<AuditLog> AddedLogs { get; } = new();

        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken)
        {
            AddedLogs.Add(auditLog);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAuditLogRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated DB failure.");
    }
}
