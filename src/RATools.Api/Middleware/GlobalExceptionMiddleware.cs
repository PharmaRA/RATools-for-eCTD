using Microsoft.AspNetCore.Mvc;

namespace RATools.Api.Middleware;

public sealed partial class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    [LoggerMessage(EventId = 5001, Level = LogLevel.Error,
        Message = "Unhandled exception while processing request {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string traceId);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var traceId = context.TraceIdentifier;
            LogUnhandledException(_logger, ex, traceId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problem = new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title = "An error occurred while processing your request.",
                Status = StatusCodes.Status500InternalServerError
            };
            problem.Extensions["traceId"] = traceId;

            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        }
    }
}
