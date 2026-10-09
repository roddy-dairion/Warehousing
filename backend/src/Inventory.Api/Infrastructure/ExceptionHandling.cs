using FluentValidation;
using Inventory.Application.Common;
using Inventory.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 9457 ProblemDetails. Unexpected errors never leak details to the client.</summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail, code, errors) = Map(exception);

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Request failed with {Status} ({Code}): {Message}", status, code, exception.Message);
        }

        httpContext.Response.StatusCode = status;
        ProblemDetails problem = errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        if (code is not null) problem.Extensions["code"] = code;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static (int Status, string Title, string? Detail, string? Code, IDictionary<string, string[]>? Errors) Map(Exception exception) => exception switch
    {
        ValidationException v => (StatusCodes.Status400BadRequest, "Validation failed", "One or more fields are invalid.", "validation_failed",
            v.Errors.GroupBy(e => JsonName(e.PropertyName)).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray())),
        AuthenticationFailedException a => (StatusCodes.Status401Unauthorized, "Unauthorized", a.Message, "auth.failed", null),
        ForbiddenException f => (StatusCodes.Status403Forbidden, "Forbidden", f.Message, "forbidden", null),
        NotFoundException n => (StatusCodes.Status404NotFound, "Not found", n.Message, "not_found", null),
        DomainRuleException d => (d.IsConflict ? StatusCodes.Status409Conflict : StatusCodes.Status422UnprocessableEntity, "Request not allowed", d.Message, d.Code, null),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflict", "This record was changed by someone else. Reload it and try again.", "concurrency_conflict", null),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "Conflict", "A record with the same unique value already exists.", "duplicate", null),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.CheckViolation } } =>
            (StatusCodes.Status409Conflict, "Conflict", "The change would break a data rule (for example, negative stock).", "constraint_violation", null),
        BadHttpRequestException b => (b.StatusCode, "Bad request", "The request could not be read.", "bad_request", null),
        OperationCanceledException => (499, "Client closed request", null, "cancelled", null),
        _ => (StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred. It has been logged.", "server_error", null),
    };

    /// <summary>"Email" → "email", "Lines[0].Quantity" → "lines[0].quantity".</summary>
    private static string JsonName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
