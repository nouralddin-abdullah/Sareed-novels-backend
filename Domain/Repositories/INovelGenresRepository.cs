using Domain.Entities;

namespace Domain.Repositories;

public interface INovelGenresRepository
{
    Task<bool> AddGenresToNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> RemoveGenresFromNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> UpdateNovelGenres(Guid novelId, IEnumerable<int> genreIds);
    Task<IEnumerable<Genre>> GetNovelGenres(Guid novelId);
    /// <summary>
    /// The genre's novels that aren't drafts and are eligible for ranking (with or without a published chapter yet),
    /// with their genres and their author (Owner, #68) loaded. isCompleted: true = completed only, false = ongoing
    /// only, null = all.
    /// </summary>
    Task<(IEnumerable<Novel>, int)> GetNovelsByGenre(int genreId, int pageSize, int pageNumber, string? sorting, bool? isCompleted);
}
