using Domain.Exceptions;
using Microsoft.Net.Http.Headers;

namespace Sareed_novels_backend.Middlewares;

/// <summary>
/// Turns exceptions into one error shape, JSON <c>{"code", "message"}</c>: <c>code</c> is stable for clients to branch
/// on, <c>message</c> is Arabic, for people. A domain exception without a code of its own gets its status's
/// (<see cref="NotFound"/>, <see cref="Forbidden"/>, ...); anything unexpected is a 500 with a generic message, logged.
/// Headers set for a successful answer before the failure ([ResponseCache], an ETag) are dropped, and the error is
/// never cached.
/// </summary>
public class ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger) : IMiddleware
{
    /// <summary>The code of a 400 without a code of its own, and of a request the API can't use (an ArgumentException).</summary>
    public const string BadRequest = "BadRequest";

    /// <summary>The code of a 403 without a code of its own.</summary>
    public const string Forbidden = "Forbidden";

    /// <summary>The code of a 404 without a code of its own.</summary>
    public const string NotFound = "NotFound";

    /// <summary>The code of a 429 without a code of its own (the rate limiter's too).</summary>
    public const string TooManyRequests = "TooManyRequests";

    /// <summary>The code of every unexpected failure (500).</summary>
    public const string ServerError = "ServerError";

    public const string InvalidRequestMessage = "تعذّر تنفيذ الطلب لأن بياناته غير صالحة.";
    public const string ServerErrorMessage = "حدث خطأ غير متوقع. حاول مرة أخرى بعد قليل.";

    /// <summary>What a spend refused for lack of points says when the handler didn't answer it itself.</summary>
    public const string InsufficientBalanceMessage = "رصيد نقاطك لا يكفي لإتمام العملية.";

    /// <summary>Response headers that only belong to a successful answer.</summary>
    private static readonly string[] SuccessOnlyHeaders =
    [
        HeaderNames.CacheControl, HeaderNames.Expires, HeaderNames.Pragma, HeaderNames.ETag, HeaderNames.LastModified,
        HeaderNames.ContentDisposition, HeaderNames.ContentLength
    ];

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next.Invoke(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var error = Describe(ex);
            if (error.Status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(ex, "An unexpected error occurred");
            }
            else if (ex is ArgumentException)
            {
                logger.LogWarning(ex, "Request refused: {Message}", ex.Message);
            }
            await Write(context, error);
        }
    }

    private sealed record Error(int Status, string Code, string Message, Dictionary<string, object?>? Extra = null);

    private static Error Describe(Exception ex) => ex switch
    {
        AccountSuspendedException suspended => new(StatusCodes.Status403Forbidden, suspended.Code!, suspended.Message, new()
        {
            ["suspendedUntil"] = suspended.Permanent ? null : suspended.SuspendedUntil,
            ["permanent"] = suspended.Permanent
        }),
        NotFoundException notFound => new(StatusCodes.Status404NotFound, notFound.Code ?? NotFound, notFound.Message),
        ForbidException forbid => new(StatusCodes.Status403Forbidden, forbid.Code ?? Forbidden, forbid.Message),
        TooManyRequestsException tooMany => new(StatusCodes.Status429TooManyRequests, tooMany.Code ?? TooManyRequests, tooMany.Message),
        BadRequestException badRequest => new(StatusCodes.Status400BadRequest, badRequest.Code, badRequest.Message),
        // A save from a copy older than the chapter (#75): the editor needs the chapter's revision now.
        ChapterChangedException changed => new(StatusCodes.Status409Conflict, changed.Code, changed.Message, new()
        {
            ["revision"] = changed.Revision
        }),
        ConflictException conflict => new(StatusCodes.Status409Conflict, conflict.Code, conflict.Message),
        // A spend that lost a race for the balance, where the handler didn't answer it itself.
        InsufficientBalanceException => new(StatusCodes.Status400BadRequest, "InsufficientBalance", InsufficientBalanceMessage),
        // A value the API can't work with (EF refusing a negative Skip used to end here as a 403): the client's fault,
        // but its message is for developers, so it is logged rather than shown.
        ArgumentException => new(StatusCodes.Status400BadRequest, BadRequest, InvalidRequestMessage),
        _ => new(StatusCodes.Status500InternalServerError, ServerError, ServerErrorMessage)
    };

    private static Task Write(HttpContext context, Error error)
    {
        var response = context.Response;
        foreach (var header in SuccessOnlyHeaders)
        {
            response.Headers.Remove(header);
        }
        response.Headers.CacheControl = "no-store";
        response.StatusCode = error.Status;

        var body = new Dictionary<string, object?> { ["code"] = error.Code, ["message"] = error.Message };
        foreach (var (name, value) in error.Extra ?? [])
        {
            body[name] = value;
        }
        return response.WriteAsJsonAsync(body);
    }
}
