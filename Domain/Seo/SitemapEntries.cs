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

/// <summary>
/// A published chapter and when it came out (<see cref="Domain.Entities.Chapter.PublishedAt"/>, #39). Null only for a
/// chapter that code from before PublishedAt published after AddChapterPublishedAt filled the dates in; the SEO worker
/// then leaves its lastmod out.
/// </summary>
public sealed record ChapterSitemapEntry(Guid Id, DateTime? LastModified);

/// <summary>A wiki entry of the novel that passes <see cref="WikiPages.IsIndexable(Domain.Entities.NovelEntity)"/>.</summary>
public sealed record WikiSitemapEntry(Guid Id, DateTime LastModified);
