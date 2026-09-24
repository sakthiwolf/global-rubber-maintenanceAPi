using System.Text;
using GlobalRubber.MMM.Application;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Api.Bootstrap;

/// <summary>
/// One-time console bootstrap for the first ADMIN user (docs/GlobalRubber_Database_Design.md
/// section 14, "Initial Administrator" - no SQL script seeds a password, on purpose). Invoked
/// via <c>dotnet run -- bootstrap-admin</c>; Program.cs detects this argument before building
/// the web host, so Kestrel never starts for this path.
///
/// Builds its own minimal generic host (DI container only - no ASP.NET Core hosting/Kestrel),
/// reusing the exact same <c>AddApplication()</c>/<c>AddInfrastructure()</c> composition roots
/// as the real app, so it shares the same DbContext, PasswordHasher and database conventions
/// rather than duplicating them. Environment resolution mirrors the normal web app's own
/// (ASPNETCORE_ENVIRONMENT), so this reads the same appsettings layering (e.g.
/// appsettings.Development.json) the real app would.
/// </summary>
public static class BootstrapAdminCommandRunner
{
    private const string AdminRoleCode = "ADMIN";
    private const string UserDocumentType = "USER";

    public static async Task<int> RunAsync()
    {
        var settings = new HostApplicationBuilderSettings
        {
            EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Production,
        };
        var builder = Host.CreateApplicationBuilder(settings);
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
        builder.Logging.ClearProviders(); // keep the interactive prompt output clean

        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        var dbContext = services.GetRequiredService<GlobalRubberDbContext>();
        var passwordHasher = services.GetRequiredService<IPasswordHasher>();
        var dateTimeProvider = services.GetRequiredService<IDateTimeProvider>();

        Console.WriteLine("Global Rubber MMM - Initial Administrator Bootstrap");
        Console.WriteLine("====================================================");

        Role adminRole;
        bool adminAlreadyExists;
        try
        {
            adminRole = await dbContext.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.RoleCode == AdminRoleCode)
                ?? throw new BootstrapAbortedException(
                    $"ERROR: The '{AdminRoleCode}' role was not found in security.role_master.{Environment.NewLine}" +
                    "Run the database seed scripts (010_seed_data.sql) before bootstrapping an admin.");

            adminAlreadyExists = await dbContext.Users.AsNoTracking().AnyAsync(u => u.RoleId == adminRole.RoleId);
        }
        catch (BootstrapAbortedException ex)
        {
            Console.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception)
        {
            // Never print the raw exception here - it can include connection details (server
            // name, login name). A short, generic message is enough for an operator to act on.
            Console.WriteLine("ERROR: Could not connect to the database. Verify ConnectionStrings:DefaultConnection and that the database is reachable, then try again.");
            return 1;
        }

        if (adminAlreadyExists)
        {
            Console.WriteLine("An Administrator user already exists. Bootstrap is not needed.");
            return 0;
        }

        string loginId;
        while (true)
        {
            loginId = Prompt("Login ID").Trim();
            if (loginId.Length == 0)
            {
                Console.WriteLine("Login ID is required.");
                continue;
            }

            var loginIdTaken = await dbContext.Users.AsNoTracking().AnyAsync(u => u.LoginId == loginId);
            if (loginIdTaken)
            {
                Console.WriteLine($"Login ID '{loginId}' is already in use. Choose another.");
                continue;
            }

            break;
        }

        var userName = PromptRequired("User Name");
        var email = Prompt("Email (optional)").Trim();
        var mobile = Prompt("Mobile (optional)").Trim();

        string password;
        while (true)
        {
            password = ReadPassword("Password");
            if (password.Length == 0)
            {
                Console.WriteLine("Password is required.");
                continue;
            }

            var confirmPassword = ReadPassword("Confirm Password");
            if (password != confirmPassword)
            {
                Console.WriteLine("Passwords do not match. Try again.");
                continue;
            }

            break;
        }

        string userCode;
        try
        {
            userCode = await GenerateNextUserCodeAsync(dbContext);
        }
        catch (Exception)
        {
            Console.WriteLine("ERROR: Could not generate a user code. Verify the database connection and try again.");
            return 1;
        }

        var passwordHash = passwordHasher.HashPassword(password);
        // password/passwordHash never get written to Console or a log from this point on.
        var now = dateTimeProvider.UtcNow;

        var user = new User
        {
            UserCode = userCode,
            LoginId = loginId,
            UserName = userName,
            PasswordHash = passwordHash,
            RoleId = adminRole.RoleId,
            Email = email.Length == 0 ? null : email,
            Mobile = mobile.Length == 0 ? null : mobile,
            MustChangePassword = true,
            FailedLoginCount = 0,
            LockoutEndAt = null,
            LastLoginAt = null,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = null,
            UpdatedAt = null,
            UpdatedBy = null,
        };

        try
        {
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to save the administrator user: {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Administrator user created successfully.");
        Console.WriteLine($"  User Code : {user.UserCode}");
        Console.WriteLine($"  Login ID  : {user.LoginId}");
        Console.WriteLine("The user must change their password on first login (must_change_password = true).");

        return 0;
    }

    /// <summary>
    /// Matches the documented convention (database/scripts/004_create_configuration_tables.sql):
    /// last_number is incremented under UPDLOCK, HOLDLOCK - never MAX(code)+1 in application
    /// code. Uses a raw parameterized query rather than a new EF entity/DbSet for this table,
    /// since nothing else in the backend reads document_sequence_configuration yet.
    /// </summary>
    private static async Task<string> GenerateNextUserCodeAsync(GlobalRubberDbContext dbContext)
    {
        var rows = await dbContext.Database.SqlQueryRaw<SequenceRow>(
            "UPDATE configuration.document_sequence_configuration WITH (UPDLOCK, HOLDLOCK) " +
            "SET last_number = last_number + 1 " +
            "OUTPUT INSERTED.last_number AS LastNumber, INSERTED.prefix AS Prefix, INSERTED.pad_length AS PadLength " +
            "WHERE document_type = {0};",
            UserDocumentType).ToListAsync();

        var row = rows.Single();
        return $"{row.Prefix}-{row.LastNumber.ToString(new string('0', row.PadLength))}";
    }

    private static string Prompt(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine() ?? string.Empty;
    }

    private static string PromptRequired(string label)
    {
        while (true)
        {
            var value = Prompt(label).Trim();
            if (value.Length > 0)
            {
                return value;
            }

            Console.WriteLine($"{label} is required.");
        }
    }

    private static string ReadPassword(string label)
    {
        Console.Write($"{label}: ");
        var password = new StringBuilder();
        ConsoleKeyInfo key;
        do
        {
            key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                    Console.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
                Console.Write('*');
            }
        } while (key.Key != ConsoleKey.Enter);

        Console.WriteLine();
        return password.ToString();
    }

    private sealed class SequenceRow
    {
        public int LastNumber { get; set; }
        public string Prefix { get; set; } = string.Empty;
        public byte PadLength { get; set; }
    }

    /// <summary>Local control-flow signal only (a known, already-formatted message to print) -
    /// distinct from an unexpected failure, which gets the generic "could not connect" message.</summary>
    private sealed class BootstrapAbortedException : Exception
    {
        public BootstrapAbortedException(string message) : base(message)
        {
        }
    }
}
