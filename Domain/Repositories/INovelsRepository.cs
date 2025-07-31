using Domain.Entities;

namespace Domain.Repositories;

public interface INovelsRepository
{
    Task<bool> CreateNovel(Novel novel);
    Task<Novel?> GetOne(Guid novelId);
    Task<bool> UpdateOne(Novel novel);
    Task<(IEnumerable<Novel?>, int)> GetWorks(string userId, int PageNumber, int PageSize);
}
