using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// #60: in a member's comment list (GET /api/User/{userName}/comments), a reply says what it answers
/// (<c>parentComment</c>: that comment's id, full text and author, as the comment lists show the author) and a
/// paragraph comment quotes its paragraph (<c>paragraphExcerpt</c>, as the comment context gives it, and only to a
/// viewer the reader would show the chapter to), read with the page and not a query per comment.
/// </summary>
public partial class ProfileListsHttpTests
{
    private const string LongParagraph =
        "<p>كان <strong>الليل</strong> طويلاً&nbsp;في القرية،<br>والريح تعوي بين البيوت القديمة كأنها تبحث عن شيء فقدته منذ زمن بعيد، " +
        "ولم يكن أحد يجرؤ على الخروج بعد غروب الشمس، إلا ذلك الغريب الذي وصل صباح أمس ولم يعرف أحد اسمه ولا من أين جاء.</p>";

    /// <summary>A comment's author as a JSON object shows them: id, userName, displayName, profilePhoto.</summary>
    private static (string? Id, string? UserName, string? DisplayName, string? ProfilePhoto) Author(JsonElement user) =>
        (user.GetProperty("id").GetString(), user.GetProperty("userName").GetString(),
            user.GetProperty("displayName").GetString(), user.GetProperty("profilePhoto").GetString());

    /// <summary>The author of <paramref name="commentId"/> as the chapter's comment list shows it to <paramref name="viewer"/>.</summary>
    private async Task<JsonElement> AuthorInThread(Guid chapterId, Guid commentId, ApiUser? viewer = null) =>
        Item(await Page($"/api/comment/chapter/{chapterId}?pageSize=50", viewer), commentId).GetProperty("user");

    /// <summary>The item <paramref name="id"/> of the member's comment list, as <paramref name="viewer"/> gets it.</summary>
    private async Task<JsonElement> Listed(ApiUser member, Guid id, ApiUser? viewer) =>
        Item(await Page(CommentsOf(member.UserName, "?pageSize=50"), viewer), id);

    private static MultipartFormDataContent WithPhoto(MultipartFormDataContent form)
    {
        var image = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "ProfilePhoto", "photo.png");
        return form;
    }

    [Fact]
    public async Task A_reply_has_the_comment_it_answers_with_its_author_and_a_top_level_comment_has_none()
    {
        var (member, other, author, someone) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, paragraph) = await ReadableNovel(author);
        // The other member has a display name and a photo of their own, so every field of the author is checked.
        await Ok(await api.Send(HttpMethod.Patch, "/api/User/update-me", other, WithPhoto(ReaderApi.Form(("DisplayName", "ريم")))));

        // Longer than any excerpt: the parent's text comes whole (the app shortens it).
        var longText = string.Join(" ", Enumerable.Repeat("رأي طويل في هذا الفصل", 20));
        var onChapter = await api.Comment(other, OnChapter(chapter.Id), longText);
        var onParagraph = await api.Comment(other, OnParagraph(paragraph.Id), "رأي في الفقرة");
        var mine = await api.Comment(member, OnChapter(chapter.Id), "تعليقي");
        var toChapterThread = await api.Comment(member, OnChapter(chapter.Id), "رد على الفصل", onChapter);
        var toParagraphThread = await api.Comment(member, OnParagraph(paragraph.Id), "رد على الفقرة", onParagraph);
        var toMyself = await api.Comment(member, OnChapter(chapter.Id), "رد على نفسي", mine);
        var onMyParagraph = await api.Comment(member, OnParagraph(paragraph.Id), "على الفقرة");

        var otherAuthor = Author(await AuthorInThread(chapter.Id, onChapter));
        Assert.Equal((other.Id, other.UserName, "ريم"), (otherAuthor.Id, otherAuthor.UserName, otherAuthor.DisplayName));
        Assert.StartsWith($"https://files.test/profile-images/{other.Id}/", otherAuthor.ProfilePhoto);
        var memberAuthor = Author(await AuthorInThread(chapter.Id, mine));

        // The same for anyone: signed out, the member, the parent's author, anyone else.
        foreach (var viewer in new[] { null, member, other, someone })
        {
            var page = await Page(CommentsOf(member.UserName, "?pageSize=50"), viewer);
            Assert.Equal(Newest([mine, toChapterThread, toParagraphThread, toMyself, onMyParagraph]), page.Ids());

            foreach (var (reply, parent, text, by) in new[]
                     {
                         (toChapterThread, onChapter, longText, otherAuthor),
                         (toParagraphThread, onParagraph, "رأي في الفقرة", otherAuthor),
                         (toMyself, mine, "تعليقي", memberAuthor)
                     })
            {
                var item = Item(page, reply);
                Assert.True(item.GetProperty("isReply").GetBoolean());
                Assert.Equal(parent, item.GetProperty("parentCommentId").GetGuid());
                var answered = item.GetProperty("parentComment");
                Assert.Equal(ParentFields, Names(answered));
                Assert.Equal(parent, answered.GetProperty("id").GetGuid());
                Assert.Equal(text, answered.GetProperty("content").GetString());
                Assert.Equal(["id", "userName", "displayName", "profilePhoto"], Names(answered.GetProperty("user")));
                Assert.Equal(by, Author(answered.GetProperty("user")));
            }

            foreach (var topLevel in new[] { mine, onMyParagraph })
            {
                Assert.False(Item(page, topLevel).GetProperty("isReply").GetBoolean());
                Assert.Equal(JsonValueKind.Null, Item(page, topLevel).GetProperty("parentComment").ValueKind);
            }
        }

        await AssertListed(member, [], Newest([mine, toChapterThread, toParagraphThread, toMyself, onMyParagraph]));
    }

    [Fact]
    public async Task A_parent_shows_its_authors_names_as_they_are_now_and_a_deleted_account_as_threads_show_it()
    {
        var (member, other, author) = (await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, _) = await ReadableNovel(author);
        var thread = await api.Comment(other, OnChapter(chapter.Id), "رأي");
        var reply = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);

        // The parent's author renames themselves, user name and display name.
        var newName = "r" + Guid.NewGuid().ToString("N")[..10];
        await Ok(await api.Send(HttpMethod.Patch, "/api/User/update-me", other,
            ReaderApi.Form(("UserName", newName), ("DisplayName", "اسم جديد"))));
        var renamed = Author(Item(await Page(CommentsOf(member.UserName)), reply).GetProperty("parentComment").GetProperty("user"));
        Assert.Equal((other.Id, newName, "اسم جديد", null), renamed);
        Assert.Equal(Author(await AuthorInThread(chapter.Id, thread)), renamed);

        // They delete their account: their comment stays in the thread under the deleted account's name, and so it
        // does here.
        Assert.Equal(HttpStatusCode.NoContent, (await api.Send(HttpMethod.Delete, "/api/User/me", other,
            JsonContent.Create(new { password = ModerationApi.Password }))).StatusCode);
        var item = Item(await Page(CommentsOf(member.UserName)), reply);
        var deleted = Author(item.GetProperty("parentComment").GetProperty("user"));
        Assert.Equal(other.Id, deleted.Id);
        Assert.StartsWith(DeletedAccounts.UserNamePrefix, deleted.UserName);
        Assert.Equal(DeletedAccounts.DisplayName, deleted.DisplayName);
        Assert.Null(deleted.ProfilePhoto);
        Assert.Equal(Author(await AuthorInThread(chapter.Id, thread)), deleted);
        Assert.Equal("رأي", item.GetProperty("parentComment").GetProperty("content").GetString());
        await AssertListed(member, [], [reply]);
    }

    [Fact]
    public async Task A_reply_to_someone_the_viewer_blocked_stays_listed_without_its_parent_so_the_total_is_the_count()
    {
        var (member, other, viewer, blockedByOther, someone) =
            (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var (_, chapter, _) = await ReadableNovel(await api.SignUp());
        var thread = await api.Comment(other, OnChapter(chapter.Id), "رأي");
        var reply = await api.Comment(member, OnChapter(chapter.Id), "رد", thread);
        var mine = await api.Comment(member, OnChapter(chapter.Id), "تعليقي");

        async Task AssertParent(ApiUser? someoneViewing, bool shown)
        {
            var page = await Page(CommentsOf(member.UserName), someoneViewing);
            Assert.Equal([mine, reply], page.Ids());
            Assert.Equal(2, page.GetProperty("totalItemsCount").GetInt32());
            var item = Item(page, reply);
            Assert.True(item.GetProperty("isReply").GetBoolean());
            Assert.Equal(thread, item.GetProperty("parentCommentId").GetGuid());
            Assert.Equal(shown ? JsonValueKind.Object : JsonValueKind.Null, item.GetProperty("parentComment").ValueKind);
            // As the thread: the chapter's comment list shows the parent to them, or leaves it out.
            var threadIds = (await Page($"/api/comment/chapter/{chapter.Id}?pageSize=50", someoneViewing)).Ids();
            Assert.Equal(shown, threadIds.Contains(thread));
            // The total is the count on the profile, as they see it and as anyone does.
            Assert.Equal(2, (await Page($"/api/User/{member.UserName}", someoneViewing)).GetProperty("commentsCount").GetInt32());
        }

        // The viewer blocks the parent's author: the reply stays, without what it answers.
        await Ok(await api.Block(viewer, other));
        await AssertParent(viewer, shown: false);
        // The parent's author blocks someone: what they wrote isn't hidden from that member, here as in the thread.
        await Ok(await api.Block(other, blockedByOther));
        await AssertParent(blockedByOther, shown: true);
        foreach (var anyone in new[] { null, member, other, someone })
        {
            await AssertParent(anyone, shown: true);
        }
        await AssertListed(member, [], [mine, reply]);

        // The member blocks the parent's author after replying: their own list loses the parent too.
        await Ok(await api.Block(member, other));
        await AssertParent(member, shown: false);
        await AssertParent(someone, shown: true);

        await Ok(await api.Unblock(viewer, other));
        await Ok(await api.Unblock(member, other));
        await AssertParent(viewer, shown: true);
        await AssertParent(member, shown: true);
    }

    [Fact]
    public async Task A_paragraph_comment_quotes_its_paragraph_as_the_comment_context_does_to_those_who_may_read_the_chapter()
    {
        var (author, member, reader, subscriber) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var (free, freeParagraphs) = await api.AddChapter(novel, "<p>الأولى</p>", LongParagraph);
        var (early, earlyParagraphs) = await api.AddChapter(novel, "<p>نص <em>مدفوع</em></p>");
        await using (var db = api.Db())
        {
            await db.Chapters.Where(c => c.Id == early.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ChapterIndex, 2));
        }

        var onFree = await api.Comment(member, OnParagraph(freeParagraphs[1].Id), "على فقرة مجانية");
        var onEarly = await api.Comment(member, OnParagraph(earlyParagraphs[0].Id), "على فقرة مبكرة");
        var thread = await api.Comment(subscriber, OnParagraph(earlyParagraphs[0].Id), "رأي");
        var replyOnEarly = await api.Comment(member, OnParagraph(earlyParagraphs[0].Id), "رد", thread);
        var onChapter = await api.Comment(member, OnChapter(free.Id), "على الفصل");

        // The second chapter is in early access: its own lock (#94, started now), and one reader has paid for it.
        await using (var db = api.Db())
        {
            await db.Chapters.Where(c => c.Id == free.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.PublishedChapterSequence, 1));
            await db.Chapters.Where(c => c.Id == early.Id).ExecuteUpdateAsync(s => s
                .SetProperty(c => c.PublishedChapterSequence, 2)
                .SetProperty(c => c.EarlyAccessFrom, DateTime.UtcNow));
            db.NovelPrivileges.Add(new NovelPrivilege { Id = Guid.NewGuid(), NovelId = novel.Id, IsEnabled = true });
            db.NovelPrivilegeSubscriptions.Add(new NovelPrivilegeSubscription
            {
                Id = Guid.NewGuid(), NovelId = novel.Id, UserId = subscriber.Id, AmountPaid = 100, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        async Task<JsonElement> ExcerptInContext(Guid commentId, ApiUser? viewer) =>
            (await (await api.Get($"/api/notifications/comment/{commentId}", viewer)).OkJson())
            .GetProperty("context").GetProperty("paragraphExcerpt");

        foreach (var (viewer, readsEarly) in new (ApiUser?, bool)[]
                 {
                     (null, false), (reader, false), (member, false), (subscriber, true), (author, true)
                 })
        {
            var page = await Page(CommentsOf(member.UserName, "?pageSize=50"), viewer);
            Assert.Equal([onChapter, replyOnEarly, onEarly, onFree], page.Ids());

            // The free chapter's paragraph, as plain text within 140 characters, for everyone.
            var freeExcerpt = Item(page, onFree).GetProperty("paragraphExcerpt").GetString()!;
            Assert.StartsWith("كان الليل طويلاً في القرية، والريح تعوي", freeExcerpt);
            Assert.EndsWith("…", freeExcerpt);
            Assert.True(freeExcerpt.Length <= 140);
            Assert.Equal(freeExcerpt, (await ExcerptInContext(onFree, viewer)).GetString());

            // The early chapter's, top-level comment and reply alike, only for those the reader shows it to.
            foreach (var onEarlyParagraph in new[] { onEarly, replyOnEarly })
            {
                var item = Item(page, onEarlyParagraph);
                Assert.Equal(earlyParagraphs[0].Id, item.GetProperty("paragraphId").GetGuid());
                var excerpt = item.GetProperty("paragraphExcerpt");
                if (readsEarly)
                {
                    Assert.Equal("نص مدفوع", excerpt.GetString());
                }
                else
                {
                    Assert.Equal(JsonValueKind.Null, excerpt.ValueKind);
                }
                Assert.Equal((await ExcerptInContext(onEarlyParagraph, viewer)).GetString(), excerpt.GetString());
            }

            // A comment on the chapter itself has no paragraph to quote.
            Assert.Equal(JsonValueKind.Null, Item(page, onChapter).GetProperty("paragraphId").ValueKind);
            Assert.Equal(JsonValueKind.Null, Item(page, onChapter).GetProperty("paragraphExcerpt").ValueKind);
        }

        // Comments on a locked chapter are still listed and counted (#54); only the quote is withheld.
        await AssertListed(member, [], [onChapter, replyOnEarly, onEarly, onFree]);
    }

    [Fact]
    public async Task A_page_reads_parents_and_paragraphs_with_its_items_not_a_query_per_comment()
    {
        var (few, many, viewer, author) = (await api.SignUp(), await api.SignUp(), await api.SignUp(), await api.SignUp());
        var novel = await api.AddNovel(author);
        var (first, firstParagraphs) = await api.AddChapter(novel, "<p>أ</p>", "<p>ب</p>", "<p>ج</p>", "<p>د</p>");
        var (second, secondParagraphs) = await api.AddChapter(novel, "<p>هـ</p>");
        await using (var db = api.Db())
        {
            await db.Chapters.Where(c => c.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(c => c.ChapterIndex, 2));
        }

        // Both members write on the same two chapters: one item on each, or twelve, half of them replies to comments
        // by six different members.
        async Task Write(ApiUser member, int repliesPerChapter, int commentsPerChapter)
        {
            foreach (var paragraph in new[] { firstParagraphs[0], secondParagraphs[0] })
            {
                for (var i = 0; i < repliesPerChapter; i++)
                {
                    var thread = await api.Comment(await api.SignUp(), OnParagraph(paragraph.Id), $"رأي {i}");
                    await api.Comment(member, OnParagraph(paragraph.Id), "رد", thread);
                }
                for (var i = 0; i < commentsPerChapter; i++)
                {
                    await api.Comment(member, OnParagraph(paragraph.Id), $"تعليق {i}");
                }
            }
        }

        await Write(few, repliesPerChapter: 1, commentsPerChapter: 0);
        await Write(many, repliesPerChapter: 3, commentsPerChapter: 3);

        var log = new CommandsByRequest();
        await using var counted = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<ApplicationDbContext>(options => options.AddInterceptors(log))));
        using var client = counted.CreateClient();

        async Task<(JsonElement Page, int Queries)> Measured(ApiUser member, ApiUser? asViewer)
        {
            var marker = Guid.NewGuid().ToString("N");
            var request = new HttpRequestMessage(HttpMethod.Get, CommentsOf(member.UserName, "?pageSize=50"));
            request.Headers.Add(CommandsByRequest.Header, marker);
            if (asViewer != null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", asViewer.Token);
            }
            var page = await (await client.SendAsync(request)).OkJson();
            return (page, log.Of(marker).Count);
        }

        // Signed in, the token's state is read once and then cached for a minute: read it before measuring.
        await Measured(few, viewer);

        foreach (var asViewer in new[] { null, viewer })
        {
            var (fewPage, fewQueries) = await Measured(few, asViewer);
            var (manyPage, manyQueries) = await Measured(many, asViewer);

            Assert.Equal(2, fewPage.Ids().Count);
            Assert.Equal(12, manyPage.Ids().Count);
            foreach (var item in manyPage.GetProperty("items").EnumerateArray().Concat(fewPage.GetProperty("items").EnumerateArray()))
            {
                Assert.NotNull(item.GetProperty("paragraphExcerpt").GetString());
                if (item.GetProperty("isReply").GetBoolean())
                {
                    Assert.NotNull(item.GetProperty("parentComment").GetProperty("user").GetProperty("userName").GetString());
                }
            }
            Assert.Equal(6, manyPage.GetProperty("items").EnumerateArray().Count(i => i.GetProperty("isReply").GetBoolean()));

            // Twelve items from six other members on two chapters are as many queries as two items: the parents and
            // their authors come with the page, and whether the viewer may read a chapter is decided once for it.
            Assert.Equal(fewQueries, manyQueries);
        }
    }
}
