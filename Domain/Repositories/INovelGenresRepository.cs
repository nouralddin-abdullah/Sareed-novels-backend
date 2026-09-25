using Domain.Entities;

namespace Domain.Repositories;

public interface INovelGenresRepository
{
    Task<bool> AddGenresToNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> RemoveGenresFromNovel(Guid novelId, IEnumerable<int> genreIds);
    Task<bool> UpdateNovelGenres(Guid novelId, IEnumerable<int> genreIds);
    Task<IEnumerable<Genre>> GetNovelGenres(Guid novelId);
    /// <summary>
    /// Novels a reader can open in this genre (not a draft, eligible for ranking, at least one published chapter),
    /// with their genres loaded. isCompleted: true = completed only, false = ongoing only, null = all.
    /// </summary>
    Task<(IEnumerable<Novel>, int)> GetNovelsByGenre(int genreId, int pageSize, int pageNumber, string? sorting, bool? isCompleted);
}
