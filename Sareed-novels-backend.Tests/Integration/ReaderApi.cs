using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Domain.Seo;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// One API (and database) for the HTTP tests of what the web and mobile apps read and post; the classes in this
/// collection run one after another, so it spins up once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ReaderApiCollection : ICollectionFixture<SardApiFactory>
{
    public const string Name = "Reader API";
}

/// <summary>A user signed up through the API, with the token it got.</summary>
public sealed record ApiUser(string Id, string UserName, string Token);

/// <summary>Sign-up, requests as a user, and direct database access for seeding what the API under test reads.</summary>
internal static class ReaderApi
{
    private static int nextIp;

    /// <summary>A client with its own address, so the per-address sign-up limit never trips.</summary>
    public static HttpClient Client(this SardApiFactory api)
    {
        var n = Interlocked.Increment(ref nextIp);
        return api.ClientFrom($"10.14.{n / 250 % 250}.{n % 250 + 1}");
    }

    public static ApplicationDbContext Db(this SardApiFactory api) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options);

    public static async Task<ApiUser> SignUp(this SardApiFactory api)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "UserName" },
            { new StringContent($"{name}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent(name), "DisplayName" }
        };
        var response = await api.Client().PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        await using var db = api.Db();
        var id = await db.Users.Where(u => u.UserName == name).Select(u => u.Id).SingleAsync();
        return new ApiUser(id, name, token);
    }

    /// <summary>Sends a request, as <paramref name="user"/> when given, anonymously otherwise.</summary>
    public static Task<HttpResponseMessage> Send(this SardApiFactory api, HttpMethod method, string url, ApiUser? user = null, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        if (user != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);
        }
        return api.Client().SendAsync(request);
    }

    public static Task<HttpResponseMessage> Get(this SardApiFactory api, string url, ApiUser? user = null) =>
        api.Send(HttpMethod.Get, url, user);

    /// <summary>The body of a response that must have succeeded.</summary>
    public static async Task<JsonElement> OkJson(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>A published novel (or a draft) by <paramref name="author"/>, with one new genre, saved directly.</summary>
    public static async Task<Novel> AddNovel(this SardApiFactory api, ApiUser author, bool isDraft = false, string? title = null)
    {
        var genre = Seed.Genre();
        var novel = Seed.Novel(new User { Id = author.Id }, title ?? "رواية " + Seed.Marker(), isDraft);
        novel.Slug = Slugs.For(novel.Id, novel.Title);
        novel.NovelGenres.Add(new NovelGenre { Novel = novel, Genre = genre });

        await using var db = api.Db();
        db.Genres.Add(genre);
        db.Novels.Add(novel);
        await db.SaveChangesAsync();
        return novel;
    }

    /// <summary>The path of a novel page by slug, escaped (slugs are mostly Arabic).</summary>
    public static string BySlug(string slug) => $"/api/novel/{Uri.EscapeDataString(slug)}";
}
