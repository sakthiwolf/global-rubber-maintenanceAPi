using System.Security.Claims;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace GlobalRubber.MMM.Api.Middlewares;

/// <summary>
/// Single place that turns any exception into the standard <see cref="ApiResponse"/> shape, so
/// controllers never need their own try/catch blocks. Registered via
/// <c>builder.Services.AddExceptionHandler&lt;GlobalExceptionHandler&gt;()</c> and
/// <c>app.UseExceptionHandler()</c> in <c>Program.cs</c>.
///
/// Only exception types that exist in this phase are mapped explicitly (validation, not found,
/// forbidden). Everything else - including anything a future business module throws that isn't
/// recognised here - falls through to a generic HTTP 500 without leaking implementation details.
///
/// The existing <see cref="ILogger{TCategoryName}"/> logging (preserved unchanged, still the
/// primary diagnostic path) is now paired with a single, targeted write to audit.application_log
/// for genuine unhandled exceptions only (the 500 branch) - that table otherwise has no writer
/// anywhere in the codebase. Expected client errors (400/403/404) are not persisted there: they
/// are normal, not technical failures. The application_log write is best-effort and never lets
/// a logging failure affect the error response already being returned to the caller.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDateTimeProvider _dateTimeProvider;

    // GlobalExceptionHandler is registered as a singleton (AddExceptionHandler<T>() default),
    // but IApplicationLogRepository is scoped (it depends on the scoped DbContext) - a scope is
    // created per exception via IServiceScopeFactory rather than injecting the repository
    // directly, which the DI container would otherwise reject at startup.
    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IServiceScopeFactory scopeFactory,
        IDateTimeProvider dateTimeProvider)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _dateTimeProvider = dateTimeProvider;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, response) = MapException(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

            await TryPersistApplicationLogAsync(httpContext, exception, cancellationToken);
        }
        else
        {
            _logger.LogWarning(exception, "{StatusCode} handling {Method} {Path}: {Message}",
                statusCode, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken);

        return true;
    }

    private async Task TryPersistApplicationLogAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var userIdClaim = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userId = int.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : (int?)null;
            var now = _dateTimeProvider.UtcNow;

            var message = exception.Message;
            if (message.Length > 2000)
            {
                message = message[..2000];
            }

            using var scope = _scopeFactory.CreateScope();
            var applicationLogRepository = scope.ServiceProvider.GetRequiredService<IApplicationLogRepository>();

            await applicationLogRepository.AddAsync(new ApplicationLog
            {
                LogLevel = "Error",
                LogDateTime = now,
                Message = message,
                Exception = exception.ToString(),
                Source = exception.Source,
                RequestPath = httpContext.Request.Path,
                HttpMethod = httpContext.Request.Method,
                UserId = userId,
                CorrelationId = httpContext.TraceIdentifier,
                MachineName = Environment.MachineName,
                CreatedAt = now,
            }, cancellationToken);
        }
        catch (Exception loggingException)
        {
            // Never let a failure to persist the technical log affect the error response the
            // caller is already receiving - the ILogger call above already captured the
            // original exception regardless of this outcome.
            _logger.LogError(loggingException, "Failed to write application_log entry.");
        }
    }

    private static (int StatusCode, ApiResponse Response) MapException(Exception exception) => exception switch
    {
        ValidationException validationException => (
            StatusCodes.Status400BadRequest,
            ApiResponse.Fail("Validation failed.", validationException.Errors)),

        NotFoundException notFoundException => (
            StatusCodes.Status404NotFound,
            ApiResponse.Fail(notFoundException.Message)),

        ConflictException conflictException => (
            StatusCodes.Status409Conflict,
            ApiResponse.Fail(conflictException.Message)),

        ForbiddenAccessException forbiddenException => (
            StatusCodes.Status403Forbidden,
            ApiResponse.Fail(forbiddenException.Message)),

        UnauthorizedAccessException => (
            StatusCodes.Status401Unauthorized,
            ApiResponse.Fail("Authentication is required to access this resource.")),

        _ => (
            StatusCodes.Status500InternalServerError,
            ApiResponse.Fail("An unexpected error occurred. Please try again later.")),
    };
}
