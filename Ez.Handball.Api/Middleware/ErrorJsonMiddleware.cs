using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Ez.Handball.Api.Middleware;

public class ErrorJsonMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorJsonMiddleware> _logger;

    public ErrorJsonMiddleware(RequestDelegate next, ILogger<ErrorJsonMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception serving {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted) throw;

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                JsonSerializer.Serialize(new { error = "internal_error" }));
        }
    }
}
