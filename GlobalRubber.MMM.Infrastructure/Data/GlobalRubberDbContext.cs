using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Data;

/// <summary>
/// The application's single EF Core context. Maps onto the existing, already-created
/// CHE_WA_Global Rubber MMM database - no migrations are generated from this context; its
/// entity configurations describe the database as it already exists.
/// </summary>
public class GlobalRubberDbContext : DbContext
{
    public GlobalRubberDbContext(DbContextOptions<GlobalRubberDbContext> options)
        : base(options)
    {
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditLogDetail> AuditLogDetails => Set<AuditLogDetail>();
    public DbSet<ApplicationLog> ApplicationLogs => Set<ApplicationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GlobalRubberDbContext).Assembly);
    }
}
