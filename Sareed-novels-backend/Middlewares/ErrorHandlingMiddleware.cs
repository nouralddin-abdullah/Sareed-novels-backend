using Domain.Exceptions;

namespace Sareed_novels_backend.Middlewares;

/// <summary>
/// Turns the domain's exceptions into their status codes. A message without a code is written as plain text (as the
/// API always has); one with a code as JSON <c>{"code", "message"}</c>, so clients can branch on the code.
/// </summary>
public class ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next.Invoke(context);
        }
        catch(AccountSuspendedException ex)
        {
            await Write(context, StatusCodes.Status403Forbidden, ex.Message, ex.Code, new()
            {
                ["suspendedUntil"] = ex.Permanent ? null : ex.SuspendedUntil,
                ["permanent"] = ex.Permanent
            });
        }
        catch(NotFoundException ex)
        {
            await Write(context, StatusCodes.Status404NotFound, ex.Message, ex.Code);
        }
        catch(ForbidException ex)
        {
            await Write(context, StatusCodes.Status403Forbidden, ex.Message, ex.Code);
        }
        catch(TooManyRequestsException ex)
        {
            await Write(context, StatusCodes.Status429TooManyRequests, ex.Message, ex.Code);
        }
        catch(BadRequestException ex)
        {
            await Write(context, StatusCodes.Status400BadRequest, ex.Message, ex.Code);
        }
        catch(ArgumentException ex)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync(ex.Message);
        }
        catch(Exception ex)
        {
            const string errorMessage = "An unexpected error occurred";
            logger.LogError(ex, errorMessage);
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Something went wrong");
        }
    }

    private static Task Write(HttpContext context, int statusCode, string message, string? code, Dictionary<string, object?>? extra = null)
    {
        context.Response.StatusCode = statusCode;
        if (code is null)
        {
            return context.Response.WriteAsync(message);
        }

        var body = new Dictionary<string, object?> { ["code"] = code, ["message"] = message };
        foreach (var (name, value) in extra ?? [])
        {
            body[name] = value;
        }
        return context.Response.WriteAsJsonAsync(body);
    }
}
