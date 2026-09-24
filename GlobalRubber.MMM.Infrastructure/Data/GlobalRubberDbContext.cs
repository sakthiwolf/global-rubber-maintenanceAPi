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
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Mold> Molds => Set<Mold>();
    public DbSet<MaintenanceType> MaintenanceTypes => Set<MaintenanceType>();
    public DbSet<SparePart> SpareParts => Set<SparePart>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AuditLogDetail> AuditLogDetails => Set<AuditLogDetail>();
    public DbSet<ApplicationLog> ApplicationLogs => Set<ApplicationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GlobalRubberDbContext).Assembly);
    }
}
