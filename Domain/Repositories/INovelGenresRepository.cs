using Domain.Entities;

namespace Domain.Repositories;

public interface INovelGenresRepository
{
    Task<bool> AddGenresToNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> RemoveGenresFromNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> UpdateNovelGenres(Guid novelId, IEnumerable<int> genreIds);
    Task<IEnumerable<Genre>> GetNovelGenres(Guid novelId);
    Task<(IEnumerable<Novel>, int)> GetNovelsByGenre(string genreSlug, int pageSize, int pageNumber, string? sorting);
}
