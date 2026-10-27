using Microsoft.AspNetCore.Mvc;

namespace Plaat.Api;

/// <summary>
/// Endpoint filter added to every API group.
/// Uncaught exceptions are logged with their stack trace
/// Responses that report a problem (400 or >500) are logged at Warning with the message the client was given.
/// </summary>
public sealed partial class ExceptionLoggingEndpointFilter(
    ILogger<ExceptionLoggingEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? string.Empty;

        object? result;

        try
        {
            result = await next(context);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                // The client hung up mid-request. Not worth logging.
                throw;
            }

            LogUnhandledException(logger, method, path, exception);

            if (httpContext.Response.HasStarted)
            {
                // The response is already on the wire, so it cannot be replaced; let the host tear
                // the connection down exactly as it did before this filter existed.
                throw;
            }

            return Results.Problem(
                title: "An unexpected error occurred.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (result is IStatusCodeHttpResult { StatusCode: { } statusCode } && IsProblemStatus(statusCode))
        {
            LogProblemResponse(logger, method, path, statusCode, GetClientMessage(result));
        }

        return result;
    }

    private static bool IsProblemStatus(int statusCode)
    {
        return statusCode
            is StatusCodes.Status400BadRequest
            or >= StatusCodes.Status500InternalServerError;
    }

    private static string GetClientMessage(object? result)
    {
        return result switch
        {
            IValueHttpResult { Value: string message } => message,
            IValueHttpResult { Value: ProblemDetails problem } => problem.Detail ?? problem.Title ?? string.Empty,
            _ => string.Empty
        };
    }

    [LoggerMessage(LogLevel.Error, "Unhandled exception in {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, string method, string path, Exception exception);

    [LoggerMessage(LogLevel.Warning, "{Method} {Path} responded {StatusCode}: {ClientMessage}")]
    private static partial void LogProblemResponse(ILogger logger, string method, string path, int statusCode, string clientMessage);
}
