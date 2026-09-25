using Domain.Entities;
using Domain.Seo;

namespace Domain.Repositories;

public interface INovelsRepository
{
    Task<bool> CreateNovel(Novel novel);
    Task<Novel?> GetOne(Guid novelId);
    Task<Novel?> GetOneBySlug(string slug);
    Task<bool> UpdateOne(Novel novel);
    /// <summary>Sets ChapterCount to the novel's current number of chapters in one SQL statement (no read-modify-write).</summary>
    Task RefreshChapterCountAsync(Guid novelId, DateTime? lastUpdatedAt = null);
    Task<(IEnumerable<Novel>, int)> GetLatestNovels(int pageSize, int pageNumber);
    Task<(IEnumerable<Novel?>, int)> GetWorks(string userId, int PageNumber, int PageSize);
    Task<(IEnumerable<Novel>, int)> GetUserPublishedWorks(string userId, int pageNumber, int pageSize);
    Task<(IEnumerable<Novel>, int)> GetAllNovelsBasicAsync(int pageNumber, int pageSize);
    Task<int> GetPublishedChaptersCountAsync(Guid novelId);
    Task<int> RecalculatePublishedSequencesAsync(Guid novelId);
    Task<List<Novel>> GetNovelsByIdsAsync(List<Guid> novelIds);
    Task<List<Novel>> GetNovelsBySharedGenresAsync(List<int> genreIds, Guid excludeNovelId, int limit);
    /// <summary>Every published novel with at least one published chapter, and those chapters, for sitemap.xml.</summary>
    Task<List<NovelSitemapEntry>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the cover URL in one UPDATE. With <paramref name="expectedUrl"/>, only while the novel still has that cover
    /// (so a background conversion never overwrites a cover the author changed meanwhile). True when a row changed.
    /// </summary>
    Task<bool> SetCoverUrlAsync(Guid novelId, string coverUrl, string? expectedUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Novels that aren't deleted and whose cover URL doesn't contain <paramref name="standardMarker"/>, ordered by id,
    /// starting after <paramref name="afterId"/>.
    /// </summary>
    Task<List<NovelCoverRef>> GetCoversNotMatchingAsync(string standardMarker, Guid? afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>Novels that aren't deleted, and how many of them have a cover URL without <paramref name="standardMarker"/>.</summary>
    Task<(int Total, int NotMatching)> CountCoversAsync(string standardMarker, CancellationToken cancellationToken = default);
}

/// <summary>A novel's cover, for maintenance jobs.</summary>
public record NovelCoverRef(Guid Id, string Title, string CoverImageUrl);
