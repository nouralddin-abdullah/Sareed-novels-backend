using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Novels.Commands.CreateNovel;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

public class NovelCreationHttpTests(SardApiFactory api) : IClassFixture<SardApiFactory>
{
    private static async Task<string> Register(HttpClient client)
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
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private async Task<int> AddGenre()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(api.ConnectionString).Options);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var genre = new Genre { Name = "Genre " + suffix, Slug = "genre-" + suffix };
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre.Id;
    }

    private static async Task<HttpResponseMessage> CreateNovel(HttpClient client, string token, int genreId, bool withCover)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("A novel title"), "Title" },
            { new StringContent("A summary that is long enough."), "Summary" },
            { new StringContent(genreId.ToString()), "GenreIds[0]" }
        };
        if (withCover)
        {
            var cover = new ByteArrayContent(new byte[1024]);
            cover.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(cover, "CoverImageUrl", "cover.png");
        }
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/myworks") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Creating_a_novel_without_a_cover_is_a_400_not_a_500()
    {
        var client = api.ClientFrom("10.9.0.1");
        var token = await Register(client);
        var genreId = await AddGenre();

        var withoutCover = await CreateNovel(client, token, genreId, withCover: false);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCover.StatusCode);
        Assert.Contains(CreateNovelCommandValidator.CoverRequiredMessage, await withoutCover.Content.ReadAsStringAsync());

        var withCover = await CreateNovel(client, token, genreId, withCover: true);
        Assert.Equal(HttpStatusCode.OK, withCover.StatusCode);
    }
}
