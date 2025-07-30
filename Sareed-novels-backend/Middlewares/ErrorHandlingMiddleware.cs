using Domain.Exceptions;

namespace Sareed_novels_backend.Middlewares;

public class ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next.Invoke(context);
        }
        catch(NotFoundException ex)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync(ex.Message);
        }
        catch(ForbidException ex)
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync(ex.Message);
        }catch(ArgumentException ex)
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
}
