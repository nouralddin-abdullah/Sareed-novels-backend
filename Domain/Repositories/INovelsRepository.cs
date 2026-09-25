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
}
