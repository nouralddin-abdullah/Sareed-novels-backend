using System.Text.Json;
using Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Unit;

public class ErrorHandlingMiddlewareTests
{
    private static async Task<(HttpResponse Response, JsonElement Body)> Run(Func<HttpContext, Task> next)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new ErrorHandlingMiddleware(NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(context, c => next(c));

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response, document.RootElement.Clone());
    }

    private static Func<HttpContext, Task> Throws(Exception ex) => _ => throw ex;

    [Theory]
    [InlineData(404, "NotFound")]
    [InlineData(403, "Forbidden")]
    [InlineData(429, "TooManyRequests")]
    public async Task A_domain_exception_without_a_code_gets_its_status_code(int status, string code)
    {
        Exception ex = status switch
        {
            404 => new NotFoundException("الرواية غير موجودة"),
            403 => new ForbidException("لا تملك هذه الرواية"),
            _ => new TooManyRequestsException("محاولات كثيرة")
        };

        var (response, body) = await Run(Throws(ex));

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(["code", "message"], body.EnumerateObject().Select(p => p.Name));
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Equal(ex.Message, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_code_given_with_the_exception_is_kept()
    {
        var (response, body) = await Run(Throws(new BadRequestException("أنت تتابع هذا المستخدم بالفعل", "AlreadyFollowing")));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("AlreadyFollowing", body.GetProperty("code").GetString());
        Assert.Equal("أنت تتابع هذا المستخدم بالفعل", body.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(ArgumentOutOfRangeException))]
    [InlineData(typeof(ArgumentNullException))]
    public async Task An_argument_exception_is_a_bad_request_not_a_forbidden_one(Type type)
    {
        // What EF throws for a negative Skip, which used to come back as 403.
        var (response, body) = await Run(Throws((Exception)Activator.CreateInstance(type, "count")!));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(ErrorHandlingMiddleware.BadRequest, body.GetProperty("code").GetString());
        // The exception's own message is for developers: logged, not shown.
        Assert.Equal(ErrorHandlingMiddleware.InvalidRequestMessage, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_unexpected_exception_is_a_500_with_a_general_arabic_message()
    {
        var (response, body) = await Run(Throws(new InvalidOperationException("Could not allocate the cover surface.")));

        Assert.Equal(500, response.StatusCode);
        Assert.Equal(ErrorHandlingMiddleware.ServerError, body.GetProperty("code").GetString());
        Assert.Equal(ErrorHandlingMiddleware.ServerErrorMessage, body.GetProperty("message").GetString());
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task A_spend_refused_for_lack_of_points_is_a_bad_request()
    {
        var (response, body) = await Run(Throws(new InsufficientBalanceException(500)));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal("InsufficientBalance", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_suspension_carries_its_end_date()
    {
        var until = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
        var (response, body) = await Run(Throws(new AccountSuspendedException(until)));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal("AccountSuspended", body.GetProperty("code").GetString());
        Assert.Equal(until, body.GetProperty("suspendedUntil").GetDateTime());
        Assert.False(body.GetProperty("permanent").GetBoolean());
    }

    [Fact]
    public async Task An_error_drops_the_headers_of_the_success_it_replaced_and_is_never_cached()
    {
        // [ResponseCache] (the sitemap's) sets Cache-Control before the action runs: its 500 was cacheable for an hour.
        var (response, _) = await Run(context =>
        {
            context.Response.Headers.CacheControl = "public,max-age=3600";
            context.Response.Headers.Expires = "Sun, 27 Sep 2026 23:00:00 GMT";
            context.Response.Headers.ETag = "\"v1\"";
            context.Response.Headers.LastModified = "Sun, 27 Sep 2026 22:00:00 GMT";
            context.Response.Headers.ContentDisposition = "attachment; filename=sitemap.json";
            context.Response.Headers.AccessControlAllowOrigin = "*";
            throw new InvalidOperationException("the database is down");
        });

        Assert.Equal(500, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.False(response.Headers.ContainsKey(HeaderNames.Expires));
        Assert.False(response.Headers.ContainsKey(HeaderNames.ETag));
        Assert.False(response.Headers.ContainsKey(HeaderNames.LastModified));
        Assert.False(response.Headers.ContainsKey(HeaderNames.ContentDisposition));
        // CORS headers stay, so the web app can still read the error.
        Assert.Equal("*", response.Headers.AccessControlAllowOrigin.ToString());
        Assert.StartsWith("application/json", response.ContentType);
    }

    [Fact]
    public async Task A_success_passes_through_untouched()
    {
        var context = new DefaultHttpContext();
        await new ErrorHandlingMiddleware(NullLogger<ErrorHandlingMiddleware>.Instance).InvokeAsync(context, c =>
        {
            c.Response.StatusCode = 200;
            c.Response.Headers.CacheControl = "public,max-age=3600";
            return Task.CompletedTask;
        });

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("public,max-age=3600", context.Response.Headers.CacheControl.ToString());
    }
}
