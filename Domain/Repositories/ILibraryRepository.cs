using Domain.Entities;

namespace Domain.Repositories;

public interface ILibraryRepository
{
    Task<(IEnumerable<UserNovelProgress>, int)> GetUserReadingProgressAsync(string userId, int pageNumber, int pageSize);
    Task<UserNovelProgress?> GetProgressAsync(string userId, Guid novelId);
    Task<bool> TrackProgressAsync(UserNovelProgress progress);
    Task<bool> UpdateProgressAsync(UserNovelProgress progress);
}
