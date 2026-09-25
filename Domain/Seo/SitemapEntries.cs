namespace Domain.Seo;

/// <summary>One public novel and its published chapters, for sitemap.xml.</summary>
public sealed record NovelSitemapEntry(string Slug, DateTime LastModified, IReadOnlyList<ChapterSitemapEntry> Chapters);

public sealed record ChapterSitemapEntry(Guid Id, DateTime LastModified);
