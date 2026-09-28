using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Repositories;
using Domain.Seo;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Sareed_novels_backend.Extensions;
using Sareed_novels_backend.Middlewares;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Errors come back in one shape, JSON {code, message} with an Arabic message: the rate limiter's 429 and a refused
/// request body too (the latter keeping the validation problem's errors).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ApiErrorsHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task The_rate_limiters_429_is_json_with_a_code_and_an_arabic_message()
    {
        // Sign-in allows 10 requests a minute per address; an unknown user is refused without locking anything.
        var client = api.ClientFrom("10.17.0.1");
        HttpResponseMessage response;
        var attempts = 0;
        do
        {
            response = await client.PostAsJsonAsync("/api/identity/Login", new { loginCardinality = "nobody" + Seed.Marker(), password = "x" });
            attempts++;
        } while (response.StatusCode != HttpStatusCode.TooManyRequests && attempts < 20);

        var body = await response.Error(HttpStatusCode.TooManyRequests);
        Assert.Equal(ErrorHandlingMiddleware.TooManyRequests, body.GetProperty("code").GetString());
        Assert.Equal(WebApplicationBuilderExtensions.TooManyRequestsMessage, body.GetProperty("message").GetString());
        Assert.True(response.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task A_refused_body_keeps_its_errors_and_gets_a_code_and_an_arabic_message()
    {
        var user = await api.SignUp();

        // No name: the reading list validator refuses it.
        var response = await api.Send(HttpMethod.Post, "/api/readinglist", user, ReaderApi.Form(("Description", "بلا اسم")));

        var body = await response.Error(HttpStatusCode.BadRequest);
        Assert.Equal(ValidationProblems.Code, body.GetProperty("code").GetString());
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("message").GetString()!);
        Assert.Matches(@"\p{IsArabic}", body.GetProperty("title").GetString()!);
        Assert.True(body.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task A_missing_item_is_json_with_a_code()
    {
        var user = await api.SignUp();

        var response = await api.Send(HttpMethod.Patch, $"/api/notifications/{Guid.NewGuid()}/read", user);

        var body = await response.Error(HttpStatusCode.NotFound);
        Assert.Equal("NotificationNotFound", body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("message").GetString()));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task The_sitemaps_failure_is_not_cacheable_like_its_success()
    {
        // [ResponseCache] sets Cache-Control: public, max-age=3600 before the action runs, so a failure used to keep it.
        var novels = Substitute.For<INovelsRepository>();
        novels.GetSitemapEntriesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<List<NovelSitemapEntry>>>(_ => throw new InvalidOperationException("the database is down"));
        await using var broken = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INovelsRepository>();
            services.AddScoped(_ => novels);
        }));

        var failed = await broken.CreateClient().GetAsync("/api/seo/sitemap");

        var body = await failed.Error(HttpStatusCode.InternalServerError);
        Assert.Equal(ErrorHandlingMiddleware.ServerError, body.GetProperty("code").GetString());
        Assert.Equal(ErrorHandlingMiddleware.ServerErrorMessage, body.GetProperty("message").GetString());
        Assert.True(failed.Headers.CacheControl?.NoStore);
        Assert.False(failed.Headers.CacheControl?.Public);
        Assert.Null(failed.Headers.CacheControl?.MaxAge);

        var succeeded = await api.Get("/api/seo/sitemap");
        Assert.Equal(JsonValueKind.Array, (await succeeded.OkJson()).ValueKind);
        Assert.True(succeeded.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromHours(1), succeeded.Headers.CacheControl?.MaxAge);
    }
}
