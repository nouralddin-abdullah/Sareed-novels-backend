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

    public async Task<Genre?> GetGenreBySlug(string slug)
    {
        return await dbContext.Genres.FirstOrDefaultAsync(g => g.Slug == slug);
    }

}
