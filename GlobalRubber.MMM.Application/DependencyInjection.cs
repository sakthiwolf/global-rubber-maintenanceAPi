using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GlobalRubber.MMM.Application;

/// <summary>
/// Composition root for the Application layer. <c>Program.cs</c> calls
/// <c>builder.Services.AddApplication()</c> and never registers application services directly,
/// so the Api project stays free of Application-layer wiring details.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IVendorService, VendorService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IMachineService, MachineService>();
        services.AddScoped<IMoldService, MoldService>();
        services.AddScoped<IMaintenanceTypeService, MaintenanceTypeService>();
        services.AddScoped<ISparePartService, SparePartService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPermissionAuthorizationService, PermissionAuthorizationService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        return services;
    }
}
