using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Covers;
using Application.Novels.Commands.CreateNovel;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>Creating a novel and changing its cover through the real API, with covers stored in memory.</summary>
public class NovelCreationHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static int nextIp;

    private static string NewIp() => $"10.9.{Interlocked.Increment(ref nextIp) / 250}.{nextIp % 250 + 1}";

    private static async Task<(string Token, string Name)> Register(HttpClient client)
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        using var form = new MultipartFormDataContent
        {
            { new StringContent(name), "UserName" },
            { new StringContent($"{name}@example.test"), "Email" },
            { new StringContent("Correct-horse-1"), "Password" },
            { new StringContent(name), "DisplayName" }
        };
        var response = await client.PostAsync("/api/identity/Register", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!, name);
    }

    private ApplicationDbContext Db() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options);

    private async Task<int> AddGenre()
    {
        await using var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var genre = new Genre { Name = "Genre " + suffix, Slug = "genre-" + suffix };
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre.Id;
    }

    private static ByteArrayContent File(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    private static async Task<HttpResponseMessage> CreateNovel(HttpClient client, string token, int genreId, ByteArrayContent? cover)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("A novel title"), "Title" },
            { new StringContent("A summary that is long enough."), "Summary" },
            { new StringContent(genreId.ToString()), "GenreIds[0]" }
        };
        if (cover is not null)
        {
            form.Add(cover, "CoverImageUrl", "cover.png");
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/myworks") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ChangeCover(HttpClient client, string token, Guid novelId, ByteArrayContent cover)
    {
        using var form = new MultipartFormDataContent { { cover, "CoverUrl", "cover.jpg" } };
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/myworks/novel-cover/{novelId}") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<string> CoverOf(Guid novelId)
    {
        await using var db = Db();
        return await db.Novels.Where(n => n.Id == novelId).Select(n => n.CoverImageUrl).SingleAsync();
    }

    private static byte[] Portrait() => TestImages.Halves(900, 1350, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Png, vertical: true);

    [Fact]
    public async Task Creating_a_novel_without_a_cover_is_a_400_not_a_500()
    {
        var client = api.ClientFrom(NewIp());
        var (token, _) = await Register(client);
        var genreId = await AddGenre();

        var withoutCover = await CreateNovel(client, token, genreId, cover: null);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCover.StatusCode);
        Assert.Contains(CreateNovelCommandValidator.CoverRequiredMessage, await withoutCover.Content.ReadAsStringAsync());

        var withCover = await CreateNovel(client, token, genreId, File(Portrait(), "image/png"));
        Assert.Equal(HttpStatusCode.OK, withCover.StatusCode);
    }

    [Fact]
    public async Task A_new_novel_gets_its_cover_in_the_standard_sizes()
    {
        var client = api.ClientFrom(NewIp());
        var (token, _) = await Register(client);

        var response = await CreateNovel(client, token, await AddGenre(), File(Portrait(), "image/png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var novelId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("novelId").GetGuid();
        var url = await CoverOf(novelId);
        Assert.True(NovelCovers.IsStandard(url), url);
        Assert.StartsWith($"https://files.test/novel-covers/{novelId}/", url);
        Assert.EndsWith("/900.webp", url);

        var folder = api.Storage.KeyOf(url)![..^"900.webp".Length];
        Assert.Equal(
            new[] { "320.webp", "640.webp", "900.webp", "cover.jpg", "og.jpg" },
            api.Storage.Objects.Keys.Where(k => k.StartsWith(folder)).Select(k => k[folder.Length..]).Order());
        Assert.Equal("image/webp", api.Storage.Objects[folder + "640.webp"].ContentType);
        Assert.Equal("image/jpeg", api.Storage.Objects[folder + "cover.jpg"].ContentType);
    }

    [Fact]
    public async Task A_file_that_is_not_an_image_is_a_400_with_a_code_whatever_its_declared_type()
    {
        var client = api.ClientFrom(NewIp());
        var (token, _) = await Register(client);
        var puts = api.Storage.Puts;

        var response = await CreateNovel(client, token, await AddGenre(), File("<html>not a picture</html>"u8.ToArray(), "image/png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CoverErrorCodes.UnsupportedFormat, body.GetProperty("errorCode").GetString());
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(puts, api.Storage.Puts);
    }

    [Fact]
    public async Task A_cover_below_the_minimum_size_is_a_400_with_a_code()
    {
        var client = api.ClientFrom(NewIp());
        var (token, _) = await Register(client);

        var response = await CreateNovel(client, token, await AddGenre(),
            File(TestImages.Halves(200, 300, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Jpeg), "image/jpeg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CoverErrorCodes.TooSmall, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Changing_a_cover_stores_a_new_standard_cover_and_keeps_the_old_files()
    {
        var client = api.ClientFrom(NewIp());
        var (token, _) = await Register(client);
        var created = await CreateNovel(client, token, await AddGenre(), File(Portrait(), "image/png"));
        var novelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("novelId").GetGuid();
        var oldUrl = await CoverOf(novelId);

        // A phone photo: landscape pixels with EXIF orientation 6, sent as JPEG.
        var photo = TestImages.WithExifOrientation(TestImages.Halves(1500, 1000, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Jpeg), 6);
        var response = await ChangeCover(client, token, novelId, File(photo, "image/jpeg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newUrl = await CoverOf(novelId);
        Assert.Equal(newUrl, body.GetProperty("coverImageUrl").GetString());
        Assert.NotEqual(oldUrl, newUrl);
        Assert.EndsWith("/960.webp", newUrl);
        Assert.True(api.Storage.Objects.ContainsKey(api.Storage.KeyOf(oldUrl)!), "old cover files are kept");

        using var stored = TestImages.Decode(api.Storage.Objects[api.Storage.KeyOf(newUrl)!].Bytes);
        Assert.Equal((960, 1440), (stored.Width, stored.Height));
        Assert.True(TestImages.Near(stored.GetPixel(480, 300), SKColors.Crimson), "turned upright: the stored left half is on top");
    }

    [Fact]
    public async Task Only_the_author_can_change_a_cover()
    {
        var client = api.ClientFrom(NewIp());
        var (author, _) = await Register(client);
        var created = await CreateNovel(client, author, await AddGenre(), File(Portrait(), "image/png"));
        var novelId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("novelId").GetGuid();
        var (stranger, _) = await Register(api.ClientFrom(NewIp()));

        var response = await ChangeCover(client, stranger, novelId, File(Portrait(), "image/png"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admins_see_the_cover_status_and_can_dry_run_the_conversion()
    {
        var client = api.ClientFrom(NewIp());
        var (_, name) = await Register(client);
        using (var scope = api.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await users.FindByNameAsync(name);
            Assert.True((await users.AddToRoleAsync(user!, UserRoles.Admin)).Succeeded);
        }
        var login = await client.PostAsJsonAsync("/api/identity/Login", new { loginCardinality = name, password = "Correct-horse-1" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

        var statusRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/covers/status");
        statusRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var status = await client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusBody = await status.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(statusBody.GetProperty("processorAvailable").GetBoolean());

        var dryRunRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/covers/convert?dryRun=true&batchSize=5");
        dryRunRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var dryRun = await client.SendAsync(dryRunRequest);
        Assert.Equal(HttpStatusCode.OK, dryRun.StatusCode);
        Assert.True((await dryRun.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("dryRun").GetBoolean());
    }
}
