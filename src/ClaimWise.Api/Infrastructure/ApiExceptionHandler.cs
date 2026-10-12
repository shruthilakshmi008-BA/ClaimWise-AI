using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClaimWise.Api.Infrastructure;

/// <summary>
/// Turns our own exceptions into consistent JSON error responses.
/// Anything it does not recognise falls through to ASP.NET's default 500 handler,
/// which hides internal details from the caller.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string title;

        switch (exception)
        {
            case NotFoundException:
                status = StatusCodes.Status404NotFound;
                title = "Not found";
                break;
            case BusinessRuleException:
                status = StatusCodes.Status409Conflict;
                title = "Business rule violated";
                break;
            default:
                return false;
        }

        logger.LogInformation("Handled {ExceptionType}: {Message}", exception.GetType().Name, exception.Message);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = exception.Message },
            cancellationToken);
        return true;
    }
}
