using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Infrastructure.Data;
using GlobalRubber.MMM.Infrastructure.Identity;
using GlobalRubber.MMM.Infrastructure.Repositories;
using GlobalRubber.MMM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GlobalRubber.MMM.Infrastructure;

/// <summary>
/// Composition root for the Infrastructure layer. <c>Program.cs</c> calls
/// <c>builder.Services.AddInfrastructure(builder.Configuration)</c> and never touches EF Core,
/// SQL Server or health-check registration directly.
/// </summary>
public static class DependencyInjection
{
    private const string DefaultConnectionName = "DefaultConnection";
    private const string DatabaseHealthCheckName = "database";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DefaultConnectionName);

        services.AddDbContext<GlobalRubberDbContext>(options =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // No connection string configured (e.g. a fresh clone before appsettings is
                // filled in). The context is still registered so DI resolves cleanly; it will
                // simply fail on first real use, which /readiness surfaces immediately.
                return;
            }

            options.UseSqlServer(connectionString, sqlServerOptions =>
            {
                sqlServerOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
                sqlServerOptions.MigrationsAssembly(typeof(GlobalRubberDbContext).Assembly.FullName);
            });
        });

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddScoped<IDocumentSequenceGenerator, DocumentSequenceGenerator>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IVendorRepository, VendorRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IMachineRepository, MachineRepository>();
        services.AddScoped<IMoldRepository, MoldRepository>();
        services.AddScoped<IMaintenanceTypeRepository, MaintenanceTypeRepository>();
        services.AddScoped<ISparePartRepository, SparePartRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IApplicationLogRepository, ApplicationLogRepository>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer), "Jwt:Issuer must be configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Audience must be configured.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Secret) && o.Secret.Length >= 32,
                "Jwt:Secret must be configured and at least 32 characters long.")
            .Validate(o => o.ExpirationMinutes > 0, "Jwt:ExpirationMinutes must be greater than 0.")
            .ValidateOnStart();

        services.AddSingleton<Application.Interfaces.IPasswordHasher, Identity.PasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        var healthChecksBuilder = services.AddHealthChecks();

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            healthChecksBuilder.AddSqlServer(
                connectionString: connectionString,
                name: DatabaseHealthCheckName,
                tags: new[] { "ready", "db" },
                timeout: TimeSpan.FromSeconds(3));
        }
        else
        {
            // Register a stand-in check that always reports Unhealthy with a clear reason,
            // instead of silently skipping the check when no connection string is configured.
            healthChecksBuilder.AddCheck(
                DatabaseHealthCheckName,
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                    "No database connection string is configured (ConnectionStrings:DefaultConnection)."),
                tags: new[] { "ready", "db" });
        }

        return services;
    }
}
