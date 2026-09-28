using Domain.Entities;

namespace Domain.Repositories;

public interface IGenresRepository
{
    Task<IEnumerable<Genre>> GetAllGenres();
    Task<Genre?> GetBySlug(string slug);

    /// <summary>
    /// Whether each of these values (distinct, ignoring case) is some genre's name or slug, compared as the database
    /// compares them, like the search's genre filter.
    /// </summary>
    Task<bool> AreAllGenresAsync(IReadOnlyCollection<string> namesOrSlugs, CancellationToken cancellationToken = default);
}
