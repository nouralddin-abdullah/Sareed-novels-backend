using Domain.Entities;

namespace Domain.Repositories;

public interface INovelsRepository
{
    Task<bool> CreateNovel(Novel novel);
    Task<Novel?> GetOne(Guid novelId);
    Task<Novel?> GetOneBySlug(string slug);
    Task<bool> UpdateOne(Novel novel);
    Task<(IEnumerable<Novel>, int)> GetLatestNovels(int pageSize, int pageNumber);
    Task<(IEnumerable<Novel?>, int)> GetWorks(string userId, int PageNumber, int PageSize);
}
