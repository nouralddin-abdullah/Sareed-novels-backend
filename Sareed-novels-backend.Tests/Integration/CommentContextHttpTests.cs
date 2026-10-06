using System.Text.Json;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/notifications/comment/{id}: where a comment is, so a notification can open the reader at its paragraph
/// and thread, and on the page of the very list that shows it (checked against that list itself).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class CommentContextHttpTests(SardApiFactory api)
{
    private static readonly DateTime Start = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string LongParagraph =
        "<p>كان <strong>الليل</strong> طويلاً&nbsp;في القرية،<br>والريح تعوي بين البيوت القديمة كأنها تبحث عن شيء فقدته منذ زمن بعيد، " +
        "ولم يكن أحد يجرؤ على الخروج بعد غروب الشمس، إلا ذلك الغريب الذي وصل صباح أمس ولم يعرف أحد اسمه ولا من أين جاء.</p>";

    /// <summary>A comment saved directly; <paramref name="minute"/> orders comments in time.</summary>
    private static Comments Comment(ApiUser user, int minute, Guid? chapterId = null, Guid? paragraphId = null,
        Guid? postId = null, Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = user.Id,
        Content = $"تعليق {minute}",
        ChapterId = chapterId,
        ParagraphId = paragraphId,
        PostId = postId,
        ParentCommentId = parentId,
        CreatedAt = Start.AddMinutes(minute)
    };

    private async Task Save(params IEnumerable<Comments>[] comments)
    {
        await using var db = api.Db();
        db.Comments.AddRange(comments.SelectMany(c => c));
        await db.SaveChangesAsync();
    }

    private async Task<JsonElement> Context(Guid commentId, int? pageSize = null)
    {
        var query = pageSize is { } size ? $"?pageSize={size}" : "";
        var detail = await (await api.Get($"/api/notifications/comment/{commentId}{query}")).OkJson();
        Assert.Equal(commentId, detail.GetProperty("comment").GetProperty("id").GetGuid());
        return detail.GetProperty("context");
    }

    /// <summary>The ids on one page of a comment list, read through the list endpoint itself (its default order).</summary>
    private async Task<List<Guid>> ListPage(string listUrl, int pageNumber, int pageSize)
    {
        var page = await (await api.Get($"{listUrl}?pageNumber={pageNumber}&pageSize={pageSize}")).OkJson();
        return page.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
    }

    private static Guid? NullableGuid(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetGuid();

    [Fact]
    public async Task A_chapter_comment_is_on_its_page_of_the_chapter_comments()
    {
        var reader = await api.SignUp();
        var novel = await api.AddNovel(await api.SignUp());
        var (chapter, paragraphs) = await api.AddChapter(novel, "<p>فقرة</p>");
        var onChapter = Enumerable.Range(0, 5).Select(i => Comment(reader, i, chapterId: chapter.Id)).ToList();
        // Newer comments that other lists show: they must not push the target to a later page.
        var onParagraph = Enumerable.Range(10, 3).Select(i => Comment(reader, i, paragraphId: paragraphs[0].Id)).ToList();
        var replies = Enumerable.Range(20, 3).Select(i => Comment(reader, i, chapterId: chapter.Id, parentId: onChapter[0].Id)).ToList();
        await Save(onChapter, onParagraph, replies);
        var target = onChapter[1]; // newest first: 4, 3 | 2, 1 | 0

        var context = await Context(target.Id, pageSize: 2);

        Assert.Equal(2, context.GetProperty("pageNumber").GetInt32());
        Assert.Contains(target.Id, await ListPage($"/api/comment/chapter/{chapter.Id}", 2, 2));
        Assert.Equal(chapter.Id, context.GetProperty("chapterId").GetGuid());
        Assert.Equal(novel.Id, context.GetProperty("novelId").GetGuid());
        Assert.Equal(novel.Slug, context.GetProperty("novelSlug").GetString());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("paragraphId").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("paragraphOrderIndex").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("paragraphExcerpt").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("parentCommentId").ValueKind);

        // Without a pageSize the lists' default of 10 applies.
        Assert.Equal(1, (await Context(target.Id)).GetProperty("pageNumber").GetInt32());
    }

    [Fact]
    public async Task A_paragraph_comment_names_its_paragraph_and_is_on_its_page_of_the_paragraph_comments()
    {
        var reader = await api.SignUp();
        var novel = await api.AddNovel(await api.SignUp());
        var (chapter, paragraphs) = await api.AddChapter(novel, "<p>الأولى</p>", LongParagraph, "<p>الثالثة</p>");
        var onParagraph = Enumerable.Range(0, 4).Select(i => Comment(reader, i, paragraphId: paragraphs[1].Id)).ToList();
        var elsewhere = Enumerable.Range(10, 3).Select(i => Comment(reader, i, chapterId: chapter.Id))
            .Concat(Enumerable.Range(20, 3).Select(i => Comment(reader, i, paragraphId: paragraphs[2].Id)))
            .ToList();
        await Save(onParagraph, elsewhere);
        var target = onParagraph[2]; // newest first: 3 | 2 | 1 | 0

        var context = await Context(target.Id, pageSize: 1);

        Assert.Equal(2, context.GetProperty("pageNumber").GetInt32());
        Assert.Equal(new[] { target.Id }, await ListPage($"/api/comment/paragraph/{paragraphs[1].Id}", 2, 1));
        Assert.Equal(paragraphs[1].Id, context.GetProperty("paragraphId").GetGuid());
        Assert.Equal(1, context.GetProperty("paragraphOrderIndex").GetInt32());
        Assert.Equal(chapter.Id, context.GetProperty("chapterId").GetGuid());
        Assert.Equal(novel.Id, context.GetProperty("novelId").GetGuid());
        Assert.Equal(JsonValueKind.Null, context.GetProperty("parentCommentId").ValueKind);

        var excerpt = context.GetProperty("paragraphExcerpt").GetString()!;
        Assert.StartsWith("كان الليل طويلاً في القرية، والريح تعوي", excerpt);
        Assert.EndsWith("…", excerpt);
        Assert.True(excerpt.Length <= 140);
        Assert.DoesNotContain("<", excerpt);
        Assert.DoesNotContain("&nbsp;", excerpt);
    }

    [Fact]
    public async Task A_reply_names_its_parent_and_is_on_its_page_of_the_thread()
    {
        var reader = await api.SignUp();
        var novel = await api.AddNovel(await api.SignUp());
        var (chapter, paragraphs) = await api.AddChapter(novel, "<p>الأولى</p>", "<p>فقرة <em>قصيرة</em></p>");
        var parent = Comment(reader, 0, paragraphId: paragraphs[1].Id);
        var others = Enumerable.Range(1, 3).Select(i => Comment(reader, 100 + i, paragraphId: paragraphs[1].Id)).ToList();
        var replies = Enumerable.Range(1, 5).Select(i => Comment(reader, i, paragraphId: paragraphs[1].Id, parentId: parent.Id)).ToList();
        await Save([parent], others, replies);
        var target = replies[3]; // oldest first: 1, 2 | 3, 4 | 5

        var context = await Context(target.Id, pageSize: 2);

        Assert.Equal(2, context.GetProperty("pageNumber").GetInt32());
        Assert.Contains(target.Id, await ListPage($"/api/comment/chapter/comments/{parent.Id}", 2, 2));
        Assert.Equal(parent.Id, context.GetProperty("parentCommentId").GetGuid());
        Assert.Equal(paragraphs[1].Id, context.GetProperty("paragraphId").GetGuid());
        Assert.Equal(1, context.GetProperty("paragraphOrderIndex").GetInt32());
        Assert.Equal("فقرة قصيرة", context.GetProperty("paragraphExcerpt").GetString());
        Assert.Equal(chapter.Id, context.GetProperty("chapterId").GetGuid());
    }

    [Fact]
    public async Task The_paragraph_excerpt_is_only_for_those_the_reader_would_show_the_chapter()
    {
        var author = await api.SignUp();
        var reader = await api.SignUp();
        var subscriber = await api.SignUp();
        var novel = await api.AddNovel(author);
        var (locked, lockedParagraphs) = await api.AddChapter(novel, "<p>نص مدفوع</p>");
        var draftNovel = await api.AddNovel(author, isDraft: true);
        var (_, draftParagraphs) = await api.AddChapter(draftNovel, "<p>نص لم يُنشر</p>");
        var onLocked = Comment(subscriber, 0, paragraphId: lockedParagraphs[0].Id);
        var onDraft = Comment(author, 0, paragraphId: draftParagraphs[0].Id);
        await using (var db = api.Db())
        {
            // Early access locks the novel's chapter (#94: its own lock, started now); one reader has paid for it.
            await db.Chapters.Where(c => c.Id == locked.Id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.PublishedChapterSequence, 1)
                .SetProperty(c => c.EarlyAccessFrom, DateTime.UtcNow));
            db.NovelPrivileges.Add(new NovelPrivilege { Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true });
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = subscriber.Id, AmountPaid = 100, IsActive = true
            });
            db.Comments.AddRange(onLocked, onDraft);
            await db.SaveChangesAsync();
        }

        async Task<JsonElement> ContextAs(Comments comment, ApiUser? viewer) =>
            (await (await api.Get($"/api/notifications/comment/{comment.Id}", viewer)).OkJson()).GetProperty("context");

        foreach (var outsider in new[] { null, reader })
        {
            var context = await ContextAs(onLocked, outsider);
            Assert.Equal(lockedParagraphs[0].Id, context.GetProperty("paragraphId").GetGuid());
            Assert.Equal(JsonValueKind.Null, context.GetProperty("paragraphExcerpt").ValueKind);
            Assert.Equal(JsonValueKind.Null, (await ContextAs(onDraft, outsider)).GetProperty("paragraphExcerpt").ValueKind);
        }
        Assert.Equal("نص مدفوع", (await ContextAs(onLocked, subscriber)).GetProperty("paragraphExcerpt").GetString());
        Assert.Equal("نص مدفوع", (await ContextAs(onLocked, author)).GetProperty("paragraphExcerpt").GetString());
        Assert.Equal("نص لم يُنشر", (await ContextAs(onDraft, author)).GetProperty("paragraphExcerpt").GetString());
    }

    [Fact]
    public async Task A_post_comment_and_a_reply_to_it_are_on_their_pages()
    {
        var poster = await api.SignUp();
        var reader = await api.SignUp();
        var post = new Post { Id = Guid.NewGuid(), UserId = poster.Id, Content = "منشور", CreatedAt = Start };
        await using (var db = api.Db())
        {
            db.Posts.Add(post);
            await db.SaveChangesAsync();
        }
        var comments = Enumerable.Range(0, 3).Select(i => Comment(reader, i, postId: post.Id)).ToList();
        var replies = Enumerable.Range(10, 2).Select(i => Comment(poster, i, postId: post.Id, parentId: comments[2].Id)).ToList();
        await Save(comments, replies);

        var comment = await Context(comments[0].Id, pageSize: 2); // newest first: 2, 1 | 0
        var reply = await Context(replies[1].Id, pageSize: 1);    // oldest first: 10 | 11

        Assert.Equal(2, comment.GetProperty("pageNumber").GetInt32());
        Assert.Contains(comments[0].Id, await ListPage($"/api/comment/post/{post.Id}", 2, 2));
        Assert.Equal(post.Id, comment.GetProperty("postId").GetGuid());
        Assert.Null(NullableGuid(comment.GetProperty("chapterId")));
        Assert.Null(NullableGuid(comment.GetProperty("paragraphId")));
        Assert.Null(NullableGuid(comment.GetProperty("parentCommentId")));

        Assert.Equal(2, reply.GetProperty("pageNumber").GetInt32());
        Assert.Equal(new[] { replies[1].Id }, await ListPage($"/api/comment/chapter/comments/{comments[2].Id}", 2, 1));
        Assert.Equal(post.Id, reply.GetProperty("postId").GetGuid());
        Assert.Equal(comments[2].Id, reply.GetProperty("parentCommentId").GetGuid());
    }
}
