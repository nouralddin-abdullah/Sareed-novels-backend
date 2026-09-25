using Infrastructure.Repositories;

namespace Sareed_novels_backend.Tests.Integration;

public class SitemapTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [Fact]
    public async Task Lists_only_public_novels_and_their_published_chapters()
    {
        var created = DateTime.UtcNow.AddDays(-10);
        await using (var db = database.CreateContext())
        {
            var author = Seed.User();
            var published = Seed.Novel(author, "published", createdAt: created);
            var draftNovel = Seed.Novel(author, "draft", isDraft: true);
            var noChapters = Seed.Novel(author, "no chapters");
            db.Users.Add(author);
            db.Novels.AddRange(published, draftNovel, noChapters);
            db.Chapters.AddRange(Seed.Chapters(published, 3, created));
            db.Chapters.AddRange(Seed.Chapters(published, 2, created, status: "Draft", startIndex: 4));
            db.Chapters.AddRange(Seed.Chapters(draftNovel, 2, created));
            db.Chapters.AddRange(Seed.Chapters(noChapters, 1, created, status: "Draft"));
            await db.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        var entries = await new NovelsRepository(read).GetSitemapEntriesAsync();

        var entry = Assert.Single(entries);
        Assert.StartsWith("published", entry.Slug[6..]);
        Assert.Equal(3, entry.Chapters.Count);
        Assert.True(entry.LastModified >= entry.Chapters.Max(c => c.LastModified));
    }
}
