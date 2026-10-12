using ClaimWise.Rag.Embeddings;
using ClaimWise.Rag.Extraction;
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
            case PayloadTooLargeException:
                status = StatusCodes.Status413PayloadTooLarge;
                title = "Payload too large";
                break;
            case UnsupportedFileException:
                status = StatusCodes.Status415UnsupportedMediaType;
                title = "Unsupported file type";
                break;
            case ExtractionException:
                status = StatusCodes.Status422UnprocessableEntity;
                title = "Document could not be read";
                break;
            case ProviderException { IsTransient: true }:
                status = StatusCodes.Status503ServiceUnavailable;
                title = "AI provider temporarily unavailable";
                break;
            case ProviderException:
                status = StatusCodes.Status502BadGateway;
                title = "AI provider error";
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
