using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Novels;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #76: <c>POST /api/myworks</c> takes an optional form field <c>isDraft</c>. True creates the novel as a draft: hidden
/// from every public list, in the author's my works, and answered with <c>isDraft</c>. Left out, the novel is public as
/// before. Also: an edit (<c>PATCH /api/myworks/{id}</c>) checks the title, summary and genres with creating's rules and
/// messages, and deleting a work answers in Arabic.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class NovelDraftHttpTests(SardApiFactory api)
{
    [Fact]
    public async Task A_novel_created_with_isDraft_is_a_draft_only_its_author_sees_until_she_publishes_it()
    {
        var author = await api.SignUp();
        var title = "مسودة " + Seed.Marker();

        var created = await (await CreateNovel(author, title, [("isDraft", "true")])).OkJson();

        Assert.True(created.GetProperty("success").GetBoolean());
        Assert.True(created.GetProperty("isDraft").GetBoolean());
        var novelId = created.GetProperty("novelId").GetGuid();
        var stored = await Stored(novelId);
        Assert.True(stored.IsDraft);
        Assert.False(stored.IsEligibleForRanking); // as PATCH .../draft leaves it: out of the rankings

        // The author has it, marked as a draft.
        Assert.True(MyWork(await (await api.Get("/api/myworks?pageSize=100", author)).OkJson(), novelId).GetProperty("isDraft").GetBoolean());
        Assert.True((await (await api.Get($"/api/myworks/{novelId}", author)).OkJson()).GetProperty("isDraft").GetBoolean());

        // Nobody else does: search, her public works, the novel page.
        Assert.DoesNotContain(novelId, await Searched(title));
        Assert.DoesNotContain(novelId, await PublicWorks(author));
        var stranger = await api.SignUp();
        await (await api.Get(ReaderApi.BySlug(stored.Slug), stranger)).Error(HttpStatusCode.NotFound);
        (await api.Get(ReaderApi.BySlug(stored.Slug), author)).EnsureSuccessStatusCode();

        // Publishing it is the same request as before.
        (await api.Send(HttpMethod.Patch, $"/api/myworks/{novelId}/publish", author)).EnsureSuccessStatusCode();
        Assert.Contains(novelId, await Searched(title));
        Assert.Contains(novelId, await PublicWorks(author));
        Assert.False(MyWork(await (await api.Get("/api/myworks?pageSize=100", author)).OkJson(), novelId).GetProperty("isDraft").GetBoolean());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task A_novel_created_without_isDraft_is_public_as_before(string? isDraft)
    {
        var author = await api.SignUp();
        var title = "رواية " + Seed.Marker();

        var created = await (await CreateNovel(author, title, isDraft is null ? [] : [("isDraft", isDraft)])).OkJson();

        Assert.False(created.GetProperty("isDraft").GetBoolean());
        var novelId = created.GetProperty("novelId").GetGuid();
        var stored = await Stored(novelId);
        Assert.False(stored.IsDraft);
        Assert.True(stored.IsEligibleForRanking);
        Assert.Contains(novelId, await Searched(title));
        Assert.Contains(novelId, await PublicWorks(author));
        Assert.False(MyWork(await (await api.Get("/api/myworks?pageSize=100", author)).OkJson(), novelId).GetProperty("isDraft").GetBoolean());
        Assert.False((await (await api.Get($"/api/myworks/{novelId}", author)).OkJson()).GetProperty("isDraft").GetBoolean());
    }

    [Fact]
    public async Task A_refused_novel_answers_isDraft_null_and_creates_nothing()
    {
        var author = await api.SignUp();
        var title = "رواية " + Seed.Marker();

        var refused = await (await CreateNovel(author, title, [("isDraft", "true")], genreId: int.MaxValue)).Error(HttpStatusCode.BadRequest);

        Assert.Equal(NovelRules.InvalidGenresCode, refused.GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("isDraft").ValueKind);
        await using var db = api.Db();
        Assert.False(await db.Novels.IgnoreQueryFilters().AnyAsync(n => n.Title == title));
    }

    [Fact]
    public async Task Editing_a_novel_refuses_a_title_summary_or_genres_with_the_messages_creating_one_gives()
    {
        var author = await api.SignUp();
        var novel = await api.AddNovel(author);
        var genreId = await AddGenre();

        // A blank title or summary used to be saved as is; an empty genre list had a message of its own.
        foreach (var (field, value) in new (string, object)[]
                 {
                     ("title", "      "), ("title", ""), ("title", "abc"), ("summary", "      "), ("summary", ""),
                     ("genreIds", Array.Empty<int>()), ("genreIds", new[] { genreId, genreId })
                 })
        {
            var edited = await (await api.Send(HttpMethod.Patch, $"/api/myworks/{novel.Id}", author,
                JsonContent.Create(new Dictionary<string, object> { [field] = value }))).Error(HttpStatusCode.BadRequest);
            var formField = char.ToUpperInvariant(field[0]) + field[1..];
            var created = await (await CreateNovel(author, "عنوان صالح", [(formField, Form(value))], genreId,
                omit: formField)).Error(HttpStatusCode.BadRequest);

            // The message the apps show is the same. Creating may also list ASP.NET's «هذا الحقل مطلوب» for a blank form
            // field (bound as null, its property is non-nullable); the validators' messages are the same for both.
            Assert.Equal("ValidationFailed", edited.GetProperty("code").GetString());
            Assert.Equal(created.GetProperty("message").GetString(), edited.GetProperty("message").GetString());
            Assert.Subset(Messages(created, formField).ToHashSet(), Messages(edited, formField).ToHashSet());
        }

        // A genre that doesn't exist: the handler's refusal, the same as creating's.
        var unknown = await (await api.Send(HttpMethod.Patch, $"/api/myworks/{novel.Id}", author,
            JsonContent.Create(new { genreIds = new[] { genreId, int.MaxValue } }))).Error(HttpStatusCode.BadRequest);
        Assert.Equal(NovelRules.InvalidGenresCode, unknown.GetProperty("code").GetString());
        Assert.Equal(NovelRules.UnknownGenreMessage, unknown.GetProperty("message").GetString());

        await using var db = api.Db();
        Assert.Equal(novel.Title, await db.Novels.Where(n => n.Id == novel.Id).Select(n => n.Title).SingleAsync());
    }

    [Fact]
    public async Task Deleting_a_work_answers_in_arabic()
    {
        var author = await api.SignUp();
        var stranger = await api.SignUp();
        var novel = await api.AddNovel(author);
        var url = $"/api/myworks/{novel.Id}/delete";

        var notTheirs = await (await api.Send(HttpMethod.Delete, url, stranger)).Error(HttpStatusCode.Forbidden);
        Assert.Equal("NotOwner", notTheirs.GetProperty("code").GetString());
        Assert.Equal("هذا الإجراء متاح لكاتب الرواية فقط", notTheirs.GetProperty("message").GetString());

        var deleted = await (await api.Send(HttpMethod.Delete, url, author)).OkJson();
        Assert.True(deleted.GetProperty("success").GetBoolean());
        Assert.Equal("حُذفت الرواية", deleted.GetProperty("message").GetString());

        var again = await (await api.Send(HttpMethod.Delete, url, author)).Error(HttpStatusCode.NotFound);
        Assert.Equal("NovelNotFound", again.GetProperty("code").GetString());
        Assert.Equal("الرواية غير موجودة", again.GetProperty("message").GetString());
    }

    /// <summary>
    /// <c>POST /api/myworks</c> as the web sends it (a multipart form with a cover), with <paramref name="extra"/> fields;
    /// <paramref name="omit"/> leaves out the regular field of that name.
    /// </summary>
    private async Task<HttpResponseMessage> CreateNovel(ApiUser author, string title, (string Name, string Value)[] extra,
        int? genreId = null, string? omit = null)
    {
        var fields = new List<(string Name, string Value)>
        {
            ("Title", title), ("Summary", "نبذة الرواية للتجربة"), ("GenreIds[0]", (genreId ?? await AddGenre()).ToString())
        };
        fields.RemoveAll(f => omit is not null && (f.Name == omit || f.Name.StartsWith(omit + "[")));
        var form = ReaderApi.Form([.. fields]);
        foreach (var (name, value) in extra)
        {
            if (name == "GenreIds")
            {
                foreach (var (id, i) in value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select((id, i) => (id, i)))
                {
                    form.Add(new StringContent(id), $"GenreIds[{i}]");
                }
                continue;
            }
            form.Add(new StringContent(value), name);
        }
        var cover = new ByteArrayContent(TestImages.Halves(900, 1350, SKColors.Crimson, SKColors.Navy, SKEncodedImageFormat.Png, vertical: true));
        cover.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(cover, "CoverImageUrl", "cover.png");
        return await api.Send(HttpMethod.Post, "/api/myworks", author, form);
    }

    /// <summary>A value of the edit's JSON as the create form sends it (genre ids comma-separated, see <see cref="CreateNovel"/>).</summary>
    private static string Form(object value) => value is int[] ids ? string.Join(',', ids) : (string)value;

    /// <summary>The validation messages of one field, in order.</summary>
    private static List<string> Messages(JsonElement problem, string field)
    {
        var errors = problem.GetProperty("errors").EnumerateObject()
            .Where(e => e.Name.Equals(field, StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => e.Value.EnumerateArray().Select(m => m.GetString()!))
            .ToList();
        Assert.NotEmpty(errors);
        return errors;
    }

    private async Task<int> AddGenre()
    {
        await using var db = api.Db();
        var genre = Seed.Genre();
        db.Genres.Add(genre);
        await db.SaveChangesAsync();
        return genre.Id;
    }

    private async Task<Novel> Stored(Guid novelId)
    {
        await using var db = api.Db();
        return await db.Novels.AsNoTracking().SingleAsync(n => n.Id == novelId);
    }

    private static JsonElement MyWork(JsonElement page, Guid novelId) =>
        Assert.Single(page.GetProperty("items").EnumerateArray(), w => w.GetProperty("id").GetGuid() == novelId);

    private async Task<List<Guid>> Searched(string title) =>
        (await (await api.Get($"/api/search/novels?query={Uri.EscapeDataString(title)}&pageSize=50")).OkJson()).Ids();

    private async Task<List<Guid>> PublicWorks(ApiUser author) =>
        (await (await api.Get($"/api/myworks/user/{author.Id}?pageSize=50")).OkJson()).Ids();
}
