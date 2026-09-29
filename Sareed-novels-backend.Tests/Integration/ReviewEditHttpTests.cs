using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// PATCH /api/{novelId}/reviews/{reviewId} (#34): the author of a review edits it in place. Before, fixing a typo or a
/// score meant deleting the review and writing it again, which lost its likes.
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class ReviewEditHttpTests(SardApiFactory api)
{
    private static string Route(Guid novelId, Guid reviewId) => $"/api/{novelId}/reviews/{reviewId}";

    private Task<HttpResponseMessage> Edit(ApiUser? user, Guid novelId, Guid reviewId, object body) =>
        api.Send(HttpMethod.Patch, Route(novelId, reviewId), user, JsonContent.Create(body));

    /// <summary>
    /// What a successful edit answers: the review itself, as an item of the novel's review list, with no
    /// { success, message, review } around it.
    /// </summary>
    private static async Task<JsonElement> EditedReview(HttpResponseMessage response)
    {
        var review = await response.OkJson();
        Assert.Equal(JsonValueKind.Object, review.ValueKind);
        Assert.True(review.TryGetProperty("id", out _), review.GetRawText());
        Assert.False(review.TryGetProperty("success", out _), review.GetRawText());
        return review;
    }

    /// <summary>A review with these scores, written through the API; its id.</summary>
    private async Task<Guid> Write(ApiUser reader, Guid novelId, decimal writing, decimal stability, decimal characters, decimal world,
        string? content = "رواية جميلة جداً", bool isSpoiler = false)
    {
        var response = await api.Send(HttpMethod.Post, $"/api/{novelId}", reader, JsonContent.Create(new
        {
            writingQualityScore = writing, updatingStabilityScore = stability, characterDevelopmentScore = characters,
            worldBuildingScore = world, isSpoiler, content
        }));
        return (await response.OkJson()).GetProperty("review").GetProperty("id").GetGuid();
    }

    private async Task<Review> Stored(Guid reviewId)
    {
        await using var db = api.Db();
        return await db.Reviews.AsNoTracking().SingleAsync(r => r.Id == reviewId);
    }

    private static void AssertSameReview(Review expected, Review actual)
    {
        Assert.Equal(expected.WritingQualityScore, actual.WritingQualityScore);
        Assert.Equal(expected.UpdatingStabilityScore, actual.UpdatingStabilityScore);
        Assert.Equal(expected.CharacterDevelopmentScore, actual.CharacterDevelopmentScore);
        Assert.Equal(expected.WorldBuildingScore, actual.WorldBuildingScore);
        Assert.Equal(expected.TotalAverageScore, actual.TotalAverageScore);
        Assert.Equal(expected.Content, actual.Content);
        Assert.Equal(expected.IsSpoiler, actual.IsSpoiler);
        Assert.Equal(expected.LikeCount, actual.LikeCount);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.UpdatedAt, actual.UpdatedAt);
    }

    private async Task<JsonElement> NovelPage(Guid novelId) => await (await api.Get($"/api/novel/by-id/{novelId}")).OkJson();

    private static void AssertAverages(JsonElement novel, decimal writing, decimal stability, decimal characters, decimal world, decimal total, int count)
    {
        Assert.Equal(writing, novel.GetProperty("averageWritingQualityScore").GetDecimal());
        Assert.Equal(stability, novel.GetProperty("averageUpdatingStabilityScore").GetDecimal());
        Assert.Equal(characters, novel.GetProperty("averageCharacterDevelopmentScore").GetDecimal());
        Assert.Equal(world, novel.GetProperty("averageWorldBuildingScore").GetDecimal());
        Assert.Equal(total, novel.GetProperty("totalAverageScore").GetDecimal());
        Assert.Equal(count, novel.GetProperty("reviewCount").GetInt32());
    }

    [Fact]
    public async Task An_edit_changes_the_novels_averages()
    {
        var author = await api.SignUp();
        var (reader, other) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 4, 4, 4);
        await Write(other, novel.Id, 2, 2, 2, 2);
        AssertAverages(await NovelPage(novel.Id), 3, 3, 3, 3, 3, count: 2);

        var edited = await EditedReview(await Edit(reader, novel.Id, review, new { writingQualityScore = 5, updatingStabilityScore = 5 }));

        Assert.Equal(4.5m, edited.GetProperty("totalAverageScore").GetDecimal());
        AssertAverages(await NovelPage(novel.Id), 3.5m, 3.5m, 3, 3, 3.25m, count: 2);
        var stored = await Stored(review);
        Assert.Equal((5m, 5m, 4m, 4m, 4.5m),
            (stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore, stored.TotalAverageScore));

        // Lower, and every score: the stats are a recount of the novel's reviews, not a nudge.
        edited = await EditedReview(await Edit(reader, novel.Id, review, new
        {
            writingQualityScore = 1, updatingStabilityScore = 1, characterDevelopmentScore = 1, worldBuildingScore = 1
        }));

        Assert.Equal(1m, edited.GetProperty("totalAverageScore").GetDecimal());
        AssertAverages(await NovelPage(novel.Id), 1.5m, 1.5m, 1.5m, 1.5m, 1.5m, count: 2);
        // The reviewer's form, filled in from her review in the list, has the new scores.
        var mine = (await (await api.Get($"/api/{novel.Id}", reader)).OkJson()).GetProperty("currentUserReview");
        Assert.Equal(review, mine.GetProperty("id").GetGuid());
        Assert.Equal(1m, mine.GetProperty("writingQualityScore").GetDecimal());
        Assert.Equal(1m, mine.GetProperty("worldBuildingScore").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, mine.GetProperty("updatedAt").ValueKind);
    }

    [Fact]
    public async Task Another_users_review_is_403_NotOwner()
    {
        var author = await api.SignUp();
        var (reader, other) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 4, 4, 4);
        var before = await Stored(review);

        // Another reader, and the novel's author: neither may edit it.
        foreach (var notTheReviewer in new[] { other, author })
        {
            var refused = await (await Edit(notTheReviewer, novel.Id, review, new { writingQualityScore = 1, content = "مراجعة من غير صاحبها" }))
                .Error(HttpStatusCode.Forbidden);

            Assert.Equal("NotOwner", refused.GetProperty("code").GetString());
            Assert.Equal("يمكنك تعديل مراجعاتك فقط", refused.GetProperty("message").GetString());
        }

        AssertSameReview(before, await Stored(review));
        AssertAverages(await NovelPage(novel.Id), 4, 4, 4, 4, 4, count: 1);
    }

    [Fact]
    public async Task An_unknown_review_is_404_ReviewNotFound()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var otherNovel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 4, 4, 4);
        var deleted = await Write(reader, otherNovel.Id, 4, 4, 4, 4);
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, $"/api/{otherNovel.Id}", reader)).StatusCode);

        var unknown = await Edit(reader, novel.Id, Guid.NewGuid(), new { content = "نص جديد للمراجعة" });
        // Her own review, under a novel it isn't about: this route has no such review.
        var elsewhere = await Edit(reader, otherNovel.Id, review, new { content = "نص جديد للمراجعة" });
        var gone = await Edit(reader, otherNovel.Id, deleted, new { content = "نص جديد للمراجعة" });

        foreach (var response in new[] { unknown, elsewhere, gone })
        {
            var refused = await response.Error(HttpStatusCode.NotFound);
            Assert.Equal("ReviewNotFound", refused.GetProperty("code").GetString());
            Assert.Equal("المراجعة غير موجودة", refused.GetProperty("message").GetString());
        }
        Assert.Equal("رواية جميلة جداً", (await Stored(review)).Content);
    }

    [Fact]
    public async Task A_partial_body_keeps_the_other_fields()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 3, 2, 5, content: "النص الأول للمراجعة", isSpoiler: true);

        // Only the text.
        await EditedReview(await Edit(reader, novel.Id, review, new { content = "النص الجديد للمراجعة" }));
        var stored = await Stored(review);
        Assert.Equal("النص الجديد للمراجعة", stored.Content);
        Assert.Equal((4m, 3m, 2m, 5m, 3.5m, true),
            (stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore,
                stored.TotalAverageScore, stored.IsSpoiler));

        // Only one score: the others and the text stay, and the review's average follows.
        await EditedReview(await Edit(reader, novel.Id, review, new { worldBuildingScore = 1 }));
        stored = await Stored(review);
        Assert.Equal((4m, 3m, 2m, 1m, 2.5m, "النص الجديد للمراجعة", true),
            (stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore,
                stored.TotalAverageScore, stored.Content, stored.IsSpoiler));

        // Only the spoiler flag, sent as null values for the rest, which are "not sent" too.
        await EditedReview(await Edit(reader, novel.Id, review, new
        {
            isSpoiler = false, writingQualityScore = (decimal?)null, content = (string?)null
        }));
        stored = await Stored(review);
        Assert.Equal((4m, 3m, 2m, 1m, 2.5m, "النص الجديد للمراجعة", false),
            (stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore,
                stored.TotalAverageScore, stored.Content, stored.IsSpoiler));

        // Empty text removes the text, as a review may be written without any.
        var withoutText = await EditedReview(await Edit(reader, novel.Id, review, new { content = "" }));
        Assert.Equal(JsonValueKind.Null, withoutText.GetProperty("content").ValueKind);
        stored = await Stored(review);
        Assert.Null(stored.Content);
        Assert.Equal((4m, 3m, 2m, 1m, false),
            (stored.WritingQualityScore, stored.UpdatingStabilityScore, stored.CharacterDevelopmentScore, stored.WorldBuildingScore, stored.IsSpoiler));
    }

    [Fact]
    public async Task An_edited_review_keeps_its_id_likes_and_date_and_comes_back_as_the_list_shows_it()
    {
        var author = await api.SignUp();
        var (reader, liker) = (await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 3, 3, 3, 3, content: "مراجعة فيها خطأ مطبعي");
        (await api.Send(HttpMethod.Post, $"/api/{novel.Id}/reviews/{review}/like", liker)).EnsureSuccessStatusCode();
        var listedBefore = (await (await api.Get($"/api/{novel.Id}?sorting=newest", reader)).OkJson()).GetProperty("reviews")[0];
        Assert.Equal(JsonValueKind.Null, listedBefore.GetProperty("updatedAt").ValueKind);
        var createdAt = (await Stored(review)).CreatedAt;
        var before = DateTime.UtcNow;

        var edited = await EditedReview(await Edit(reader, novel.Id, review, new
        {
            writingQualityScore = 4, updatingStabilityScore = 4, characterDevelopmentScore = 4, worldBuildingScore = 4,
            content = "مراجعة بلا خطأ مطبعي", isSpoiler = false
        }));

        Assert.Equal(review, edited.GetProperty("id").GetGuid());
        Assert.Equal(1, edited.GetProperty("likeCount").GetInt32());
        Assert.Equal(listedBefore.GetProperty("createdAt").GetRawText(), edited.GetProperty("createdAt").GetRawText());
        Assert.Equal("مراجعة بلا خطأ مطبعي", edited.GetProperty("content").GetString());
        Assert.Equal(reader.Id, edited.GetProperty("reviewer").GetProperty("id").GetString());
        var stored = await Stored(review);
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(1, stored.LikeCount);
        Assert.NotNull(stored.UpdatedAt);
        Assert.InRange(stored.UpdatedAt.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));

        // The whole answer is the list's item, the same JSON, for her; and for the reader who liked it, whose like is
        // still there.
        Assert.Equal(
            new[] { "reviewer", "id", "totalAverageScore", "content", "isSpoiler", "likeCount", "isLikedByCurrentUser", "createdAt", "updatedAt" },
            edited.EnumerateObject().Select(p => p.Name));
        var listed = (await (await api.Get($"/api/{novel.Id}?sorting=newest", reader)).OkJson()).GetProperty("reviews")[0];
        Assert.Equal(listed.GetRawText(), edited.GetRawText());
        var forLiker = (await (await api.Get($"/api/{novel.Id}?sorting=newest", liker)).OkJson()).GetProperty("reviews")[0];
        Assert.True(forLiker.GetProperty("isLikedByCurrentUser").GetBoolean());
        Assert.Equal(edited.GetProperty("updatedAt").GetRawText(), forLiker.GetProperty("updatedAt").GetRawText());
    }

    [Fact]
    public async Task Saving_without_a_change_does_not_mark_the_review_edited()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 3, 2, 5, content: "رواية جميلة جداً", isSpoiler: true);
        var before = await Stored(review);

        // The form saved as it was, and an empty body.
        var same = await EditedReview(await Edit(reader, novel.Id, review, new
        {
            writingQualityScore = 4, updatingStabilityScore = 3, characterDevelopmentScore = 2, worldBuildingScore = 5,
            content = "رواية جميلة جداً", isSpoiler = true
        }));
        var empty = await EditedReview(await Edit(reader, novel.Id, review, new { }));

        Assert.Equal(JsonValueKind.Null, same.GetProperty("updatedAt").ValueKind);
        Assert.Equal(same.GetRawText(), empty.GetRawText());
        AssertSameReview(before, await Stored(review));
    }

    [Fact]
    public async Task Sent_fields_are_checked_as_when_writing_a_review()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 4, 4, 4);
        var before = await Stored(review);

        var cases = new (object Body, string Field, string Message)[]
        {
            (new { writingQualityScore = 6 }, "WritingQualityScore", "تقييم جودة الكتابة يجب أن يكون من 1 إلى 5"),
            (new { updatingStabilityScore = 0 }, "UpdatingStabilityScore", "تقييم استقرار التحديثات يجب أن يكون من 1 إلى 5"),
            (new { characterDevelopmentScore = 5.5 }, "CharacterDevelopmentScore", "تقييم بناء الشخصيات يجب أن يكون من 1 إلى 5"),
            (new { worldBuildingScore = -1 }, "WorldBuildingScore", "تقييم بناء العالم القصصي يجب أن يكون من 1 إلى 5"),
            (new { content = "قصير" }, "Content", "المراجعة قصيرة جدًا. اكتب 5 أحرف على الأقل أو اتركها فارغة."),
            (new { content = new string('ن', 2001) }, "Content", "يجب ألا تتجاوز المراجعة 2000 حرف"),
        };
        foreach (var (body, field, message) in cases)
        {
            var refused = await (await Edit(reader, novel.Id, review, body)).Error(HttpStatusCode.BadRequest);

            Assert.Equal("ValidationFailed", refused.GetProperty("code").GetString());
            Assert.Equal(message, refused.GetProperty("message").GetString());
            Assert.Equal(message, refused.GetProperty("errors").GetProperty(field)[0].GetString());
        }

        // Writing a review answers the same.
        var other = await api.SignUp();
        var create = await (await api.Send(HttpMethod.Post, $"/api/{novel.Id}", other, JsonContent.Create(new
        {
            writingQualityScore = 6, updatingStabilityScore = 4, characterDevelopmentScore = 4, worldBuildingScore = 4
        }))).Error(HttpStatusCode.BadRequest);
        Assert.Equal("تقييم جودة الكتابة يجب أن يكون من 1 إلى 5", create.GetProperty("errors").GetProperty("WritingQualityScore")[0].GetString());

        AssertSameReview(before, await Stored(review));
    }

    [Fact]
    public async Task Editing_needs_a_signed_in_user()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var novel = await api.AddNovel(author);
        var review = await Write(reader, novel.Id, 4, 4, 4, 4);

        var response = await Edit(null, novel.Id, review, new { content = "نص جديد للمراجعة" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("رواية جميلة جداً", (await Stored(review)).Content);
    }
}
