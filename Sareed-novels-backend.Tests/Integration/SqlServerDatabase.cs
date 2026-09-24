using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// A throwaway SQL Server database per test class. Uses LocalDB by default; set SARD_TEST_SQL to a server connection
/// string without a database (e.g. a Docker SQL Server in CI) to run elsewhere.
/// </summary>
public class SqlServerDatabase : IAsyncLifetime
{
    private const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

    public string ConnectionString { get; } =
        $"{Environment.GetEnvironmentVariable("SARD_TEST_SQL") ?? DefaultServer};Database=SardTests_{Guid.NewGuid():N}";

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options);

    public virtual async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

/// <summary>Small builders for valid entities; each call makes unique names so tests don't collide.</summary>
internal static class Seed
{
    public static string Marker() => "x" + Guid.NewGuid().ToString("N")[..8];

    public static User User(string? displayName = null, string? userName = null)
    {
        userName ??= "user" + Guid.NewGuid().ToString("N")[..10];
        return new User
        {
            Id = Guid.NewGuid().ToString(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@test.local",
            NormalizedEmail = $"{userName}@test.local".ToUpperInvariant(),
            DisplayName = displayName ?? userName,
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Genre Genre(string? name = null)
    {
        name ??= "Genre" + Guid.NewGuid().ToString("N")[..8];
        return new Genre { Name = name, Slug = name.ToLowerInvariant() };
    }

    public static Novel Novel(User author, string title, bool isDraft = false, DateTime? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        AuthorId = author.Id,
        Title = title,
        Slug = Guid.NewGuid().ToString("N")[..5] + "-" + title.Replace(' ', '-'),
        Summary = "summary",
        CoverImageUrl = "https://example.test/cover.png",
        Status = "Ongoing",
        CreatedAt = createdAt ?? DateTime.UtcNow,
        LastUpdatedAt = createdAt ?? DateTime.UtcNow,
        IsDraft = isDraft
    };

    public static List<Chapter> Chapters(Novel novel, int count, DateTime createdAt, string status = "Published", int startIndex = 1) =>
        Enumerable.Range(startIndex, count).Select(i => new Chapter
        {
            Id = Guid.NewGuid(),
            NovelId = novel.Id,
            Title = $"Chapter {i}",
            Slug = $"chapter-{i}-{Guid.NewGuid():N}",
            Content = "text",
            Status = status,
            ChapterIndex = i,
            CreatedAt = createdAt.AddMinutes(i)
        }).ToList();

    public static UserNovelProgress Progress(User reader, Chapter chapter, int chapterNumber, DateTime lastReadAt) => new()
    {
        UserId = reader.Id,
        NovelId = chapter.NovelId,
        LastReadChapterId = chapter.Id,
        LastReadChapterNumber = chapterNumber,
        LastReadAt = lastReadAt,
        CreatedAt = lastReadAt
    };
}

/// <summary>A clock tests control.</summary>
internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
}
