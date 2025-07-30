
using System.Diagnostics;

namespace Sareed_novels_backend.Middlewares;

public class ExecutedTimeMiddleware(ILogger<ExecutedTimeMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds >= 4000)
        {
            logger.LogInformation("Request: {Method} at path: {Path} responded with status code: {StatusCode}, took: {ElapsedMilliseconds}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
