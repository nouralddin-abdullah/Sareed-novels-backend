using Domain.Entities;
using Infrastructure.Repositories;

namespace Sareed_novels_backend.Tests.Integration;

public class SitemapTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    // Well past WikiPages.MinLetters.
    private static readonly string LongText = string.Join(' ', Enumerable.Repeat("قائد ثوري شاب وذكي يواجه أعداءه بجرأة", 10));

    private static readonly DateTime WikiCreated = DateTime.UtcNow.AddDays(-30);
    private static int wikiOrder;

    [Fact]
    public async Task Lists_only_public_novels_and_their_published_chapters()
    {
        var created = DateTime.UtcNow.AddDays(-10);
        var author = Seed.User();
        var published = Seed.Novel(author, "published", createdAt: created);
        var draftNovel = Seed.Novel(author, "draft", isDraft: true);
        var noChapters = Seed.Novel(author, "no chapters");
        await using (var db = database.CreateContext())
        {
            db.Users.Add(author);
            db.Novels.AddRange(published, draftNovel, noChapters);
            db.Chapters.AddRange(Seed.Chapters(published, 3, created));
            db.Chapters.AddRange(Seed.Chapters(published, 2, created, status: "Draft", startIndex: 4));
            db.Chapters.AddRange(Seed.Chapters(draftNovel, 2, created));
            db.Chapters.AddRange(Seed.Chapters(noChapters, 1, created, status: "Draft"));
            await db.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        var ours = new[] { published.Id, draftNovel.Id, noChapters.Id };
        var entries = (await new NovelsRepository(read).GetSitemapEntriesAsync()).Where(e => ours.Contains(e.Id)).ToList();

        var entry = Assert.Single(entries);
        Assert.Equal(published.Id, entry.Id);
        Assert.Equal(published.Slug, entry.Slug);
        Assert.Equal(3, entry.Chapters.Count);
        Assert.True(entry.LastModified >= entry.Chapters.Max(c => c.LastModified));
        Assert.Empty(entry.Wiki);
    }

    [Fact]
    public async Task A_chapters_lastmod_is_when_it_came_out_and_the_novels_is_its_newest()
    {
        // #39: a chapter's page came out when the chapter was published, not when it was written.
        var written = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var author = Seed.User();
        var novel = Seed.Novel(author, "sitemap dates", createdAt: written.AddDays(-30));
        var chapters = Seed.Chapters(novel, 4, written);
        var (createdPublished, publishedLater, draft, undated) = (chapters[0], chapters[1], chapters[2], chapters[3]);
        publishedLater.PublishedAt = written.AddDays(5); // a draft written on day 0, published on day 5
        draft.Status = "Draft"; // written after the others, never published
        draft.PublishedAt = null;
        draft.CreatedAt = written.AddDays(7);
        undated.PublishedAt = null; // only code from before PublishedAt could publish one without it
        await using (var db = database.CreateContext())
        {
            db.Users.Add(author);
            db.Novels.Add(novel);
            db.Chapters.AddRange(chapters);
            await db.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        var entry = Assert.Single(await new NovelsRepository(read).GetSitemapEntriesAsync(), e => e.Id == novel.Id);

        Assert.Equal(
            [(createdPublished.Id, (DateTime?)createdPublished.CreatedAt), (publishedLater.Id, written.AddDays(5)), (undated.Id, null)],
            entry.Chapters.Select(c => (c.Id, c.LastModified)));
        // The newest chapter out, on day 5; the novel's LastUpdatedAt is older, and the draft doesn't count.
        Assert.Equal(written.AddDays(5), entry.LastModified);
    }

    [Fact]
    public async Task Carries_the_author_the_genres_and_only_the_indexable_wiki_entries()
    {
        var created = DateTime.UtcNow.AddDays(-5);
        var author = Seed.User(userName: "author" + Guid.NewGuid().ToString("N")[..8]);
        var fantasy = Seed.Genre();
        var mystery = Seed.Genre();
        var novel = Seed.Novel(author, "wiki novel", createdAt: created);
        var draftNovel = Seed.Novel(author, "wiki draft", isDraft: true, createdAt: created);

        var described = Entity(novel, "ليون", description: LongText, updatedAt: created.AddDays(1));
        var byArticle = Entity(novel, "ستارفيل", updatedAt: created);
        var articleEdited = created.AddDays(2);
        byArticle.Articles.Add(new EntityArticle { Title = "التاريخ", Content = $"<p>{LongText}</p>", UpdatedAt = articleEdited });
        var thin = Entity(novel, "راع", shortDescription: "الحاكم الساقط");
        var placeholder = Entity(novel, "_section_الأماكن", description: LongText);
        var dot = Entity(novel, ".", description: LongText);
        var deleted = Entity(novel, "محذوف", description: LongText);
        deleted.IsDeleted = true;
        var deletedArticleOnly = Entity(novel, "مقال محذوف");
        deletedArticleOnly.Articles.Add(new EntityArticle { Title = "قديم", Content = LongText, IsDeleted = true });
        var inDraft = Entity(draftNovel, "سر", description: LongText);

        await using (var db = database.CreateContext())
        {
            db.Users.Add(author);
            db.Genres.AddRange(fantasy, mystery);
            db.Novels.AddRange(novel, draftNovel);
            db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, Genre = mystery });
            db.NovelGenres.Add(new NovelGenre { NovelId = novel.Id, Genre = fantasy });
            db.Chapters.AddRange(Seed.Chapters(novel, 2, created));
            db.Chapters.AddRange(Seed.Chapters(draftNovel, 2, created));
            db.NovelEntities.AddRange(described, byArticle, thin, placeholder, dot, deleted, deletedArticleOnly, inDraft);
            await db.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        var entries = await new NovelsRepository(read).GetSitemapEntriesAsync();

        Assert.DoesNotContain(entries, e => e.Id == draftNovel.Id);
        var entry = Assert.Single(entries, e => e.Id == novel.Id);
        Assert.Equal(author.UserName, entry.AuthorUserName);
        Assert.Equal(new[] { fantasy, mystery }.OrderBy(g => g.Id).Select(g => g.Slug), entry.Genres);

        // In wiki (creation) order; an entry changes when it or one of its articles is edited.
        Assert.Equal([described.Id, byArticle.Id], entry.Wiki.Select(w => w.Id));
        Assert.Equal(described.UpdatedAt, entry.Wiki[0].LastModified, TimeSpan.FromMilliseconds(1));
        Assert.Equal(articleEdited, entry.Wiki[1].LastModified, TimeSpan.FromMilliseconds(1));
    }

    private static NovelEntity Entity(
        Novel novel, string name, string? shortDescription = null, string? description = null, DateTime? updatedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        NovelId = novel.Id,
        Section = "الشخصيات",
        Name = name,
        ShortDescription = shortDescription,
        Description = description,
        CreatedAt = WikiCreated.AddSeconds(Interlocked.Increment(ref wikiOrder)),
        UpdatedAt = updatedAt ?? WikiCreated
    };
}
