using System.Net;
using System.Net.Http.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// Novel search by genre (#25): a genre that doesn't exist is a 404 GenreNotFound, as the genre's own page answers,
/// not an empty list that reads as "no novels in this genre".
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class SearchGenreHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task An_unknown_genre_is_a_404_like_the_genre_page_and_known_ones_search_by_name_or_slug()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        await api.AddChapter(novel, "<p>فقرة</p>");
        Genre genre;
        await using (var db = api.Db())
        {
            genre = await db.NovelGenres.Where(ng => ng.NovelId == novel.Id).Select(ng => ng.Genre).SingleAsync();
        }

        var unknown = "no-such-genre-" + Seed.Marker();
        var genrePage = await (await api.Get($"/api/genre/{unknown}/novels")).Error(HttpStatusCode.NotFound);
        foreach (var url in new[] { $"/api/search/novels?genres={unknown}", $"/api/search/novels?genres={genre.Slug}&genres={unknown}" })
        {
            var error = await (await api.Get(url)).Error(HttpStatusCode.NotFound);
            Assert.Equal("GenreNotFound", error.GetProperty("code").GetString());
            Assert.Equal(genrePage.GetProperty("code").GetString(), error.GetProperty("code").GetString());
            Assert.Equal(genrePage.GetProperty("message").GetString(), error.GetProperty("message").GetString());
        }
        var posted = await api.Send(HttpMethod.Post, "/api/search/novels", content: JsonContent.Create(new { genres = new[] { unknown } }));
        Assert.Equal("GenreNotFound", (await posted.Error(HttpStatusCode.NotFound)).GetProperty("code").GetString());

        // Known genres still search: by slug, by name (in another case), both, or with a blank value (ignored).
        foreach (var query in new[]
                 {
                     $"genres={genre.Slug}",
                     $"genres={Uri.EscapeDataString(genre.Name.ToUpperInvariant())}",
                     $"genres={genre.Slug}&genres={Uri.EscapeDataString(genre.Name)}",
                     $"genres={genre.Slug}&genres=",
                 })
        {
            var page = await (await api.Get($"/api/search/novels?{query}&pageSize=50")).OkJson();
            Assert.Equal([novel.Id], page.Ids());
        }
    }
}
