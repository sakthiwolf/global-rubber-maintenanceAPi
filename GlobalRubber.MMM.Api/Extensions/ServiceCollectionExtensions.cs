using System.Text;
using GlobalRubber.MMM.Api.Configuration;
using GlobalRubber.MMM.Api.HealthChecks;
using GlobalRubber.MMM.Api.Middlewares;
using GlobalRubber.MMM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace GlobalRubber.MMM.Api.Extensions;

/// <summary>
/// Presentation-layer service registration, kept out of <c>Program.cs</c> so it stays a short,
/// readable composition of well-named steps.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<SwaggerOptions>()
            .Bind(configuration.GetSection(SwaggerOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.SectionName))
            .ValidateOnStart();

        services.AddControllers();
        services.AddEndpointsApiExplorer();

        services.AddSwaggerFoundation(configuration);
        services.AddCorsFoundation(configuration);
        services.AddExceptionHandlingFoundation();
        services.AddJwtAuthenticationFoundation(configuration);

        // Liveness never depends on external resources - it only proves the process is alive.
        services.AddHealthChecks()
            .AddCheck<SelfHealthCheck>("self", tags: new[] { "live" });

        return services;
    }

    private static IServiceCollection AddSwaggerFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        var swagger = configuration.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>() ?? new SwaggerOptions();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(swagger.Version, new OpenApiInfo
            {
                Title = swagger.Title,
                Version = swagger.Version,
                Description = swagger.Description,
                Contact = string.IsNullOrWhiteSpace(swagger.ContactName)
                    ? null
                    : new OpenApiContact { Name = swagger.ContactName, Email = swagger.ContactEmail },
            });

            // CLAUDE.md section 17: JWT must be configurable in Swagger for dev/testing.
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste only the token - Swagger adds the \"Bearer \" prefix itself.",
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
                    },
                    Array.Empty<string>()
                },
            });
        });

        return services;
    }

    /// <summary>
    /// Validates issuer, audience, signing key and lifetime - none of these are disabled. Reads
    /// the same JwtOptions class Infrastructure's JwtTokenService binds (Api already references
    /// Infrastructure), so token generation and validation can never drift out of sync.
    /// </summary>
    private static IServiceCollection AddJwtAuthenticationFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidateLifetime = true,
                };
            });

        return services;
    }

    private static IServiceCollection AddCorsFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        var cors = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();

        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (cors.AllowedOrigins.Length > 0)
                {
                    // Explicit origin allow-list, never AllowAnyOrigin() combined with credentials.
                    policy.WithOrigins(cors.AllowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                }
                else
                {
                    // No origins configured (e.g. a bare environment): deny cross-origin calls
                    // rather than silently allowing everything.
                    policy.WithOrigins(Array.Empty<string>());
                }
            });
        });

        return services;
    }

    private static IServiceCollection AddExceptionHandlingFoundation(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }

    public const string CorsPolicyName = "DefaultCorsPolicy";
}
