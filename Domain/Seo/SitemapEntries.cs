namespace Domain.Seo;

/// <summary>
/// One public novel and its published chapters, for sitemap.xml. <see cref="Id"/>, <see cref="AuthorUserName"/>,
/// <see cref="Genres"/> and <see cref="Wiki"/> came later (the author's profile, the genre pages and the novel's wiki
/// pages); the SEO worker still reads responses without them.
/// </summary>
public sealed record NovelSitemapEntry(
    string Slug,
    DateTime LastModified,
    IReadOnlyList<ChapterSitemapEntry> Chapters,
    Guid Id,
    string? AuthorUserName,
    IReadOnlyList<string> Genres,
    IReadOnlyList<WikiSitemapEntry> Wiki);

public sealed record ChapterSitemapEntry(Guid Id, DateTime LastModified);

/// <summary>A wiki entry of the novel that passes <see cref="WikiPages.IsIndexable(Domain.Entities.NovelEntity)"/>.</summary>
public sealed record WikiSitemapEntry(Guid Id, DateTime LastModified);
