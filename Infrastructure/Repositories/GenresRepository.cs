using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class GenresRepository(ApplicationDbContext dbContext) : IGenresRepository
{
    public async Task<IEnumerable<Genre>> GetAllGenres()
    {
        return await dbContext.Genres.ToListAsync();
    }

    public async Task<Genre?> GetBySlug(string slug)
    {
        return await dbContext.Genres.FirstOrDefaultAsync(g => g.Slug == slug);
    }

    public async Task<bool> AreAllGenresAsync(IReadOnlyCollection<string> namesOrSlugs, CancellationToken cancellationToken = default)
    {
        // How many of the values some genre has as its name or slug: one query, with the database's comparison.
        var known = await dbContext.Genres
            .AsNoTracking()
            .SelectMany(g => namesOrSlugs.Where(value => value == g.Name || value == g.Slug))
            .Distinct()
            .CountAsync(cancellationToken);
        return known == namesOrSlugs.Count;
    }

    public async Task<Genre?> GetGenreBySlug(string slug)
    {
        return await dbContext.Genres.FirstOrDefaultAsync(g => g.Slug == slug);
    }

}
