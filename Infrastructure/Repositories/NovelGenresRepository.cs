using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class NovelGenresRepository(ApplicationDbContext dbContext) : INovelGenresRepository
{
    public async Task<bool> AddGenresToNovel(Guid novelId, IEnumerable<int> genreIds)
    {
        try
        {
            var novelExists = await dbContext.Novels.AnyAsync(n => n.Id == novelId);
            if (!novelExists) return false;

            var existingNovelGenres = await dbContext.NovelGenres.Where(ng => ng.NovelId == novelId).Select(ng => ng.GenreId).ToListAsync();
            var newGenreIds = genreIds.Except(existingNovelGenres).ToList();
            if (newGenreIds.Count == 0) return true;
            var validGenreIds = await dbContext.Genres
                .Where(g => newGenreIds.Contains(g.Id)).Select(g => g.Id).ToListAsync();
            if (validGenreIds.Count != newGenreIds.Count) return false; // some requested to add genres doesn't exist
            var totalGenresAfter = existingNovelGenres.Count + newGenreIds.Count;
            if (totalGenresAfter > 4) return false;
            var novelGenres = newGenreIds.Select(genreId => new NovelGenre
            {
                NovelId = novelId,
                GenreId = genreId,
                AddedAt = DateTime.UtcNow
            });
            await dbContext.NovelGenres.AddRangeAsync(novelGenres);
            var result = await dbContext.SaveChangesAsync();
            return result > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<Genre>> GetNovelGenres(Guid novelId)
    {
        return await dbContext.NovelGenres.Where(ng => ng.NovelId == novelId).Include(g => g.Genre).Select(g => g.Genre).ToListAsync();
    }

    public async Task<(IEnumerable<Novel>, int)> GetNovelsByGenre(string genreSlug, int pageSize, int pageNumber, string? sorting)
    {
        var query = dbContext.NovelGenres
            .Where(ng => ng.Genre.Slug == genreSlug)
            .Include(ng => ng.Novel)
            .Select(ng => ng.Novel)
            .Distinct();

        var totalCount = await query.CountAsync();

        if (pageNumber > 0 && pageSize > 0)
        {
            // Apply sorting
            query = sorting?.ToLower() switch
            {
                "newest" => query.OrderByDescending(n => n.CreatedAt),
                "oldest" => query.OrderBy(n => n.CreatedAt),
                "rating" => query.OrderByDescending(n => n.TotalAverageScore),
                "rating_asc" => query.OrderBy(n => n.TotalAverageScore),
                "popular" => query.OrderByDescending(n => n.TotalViews),
                "reviews" => query.OrderByDescending(n => n.ReviewCount),
                _ => query.OrderByDescending(n => n.TotalViews)
            };

            query = query.Skip(pageSize * (pageNumber - 1)).Take(pageSize);
        }

        var novels = await query.ToListAsync();
        return (novels, totalCount);
    }

    public async Task<bool> RemoveGenresFromNovel(Guid novelId, IEnumerable<int> genreIds)
    {
        try
        {
            var genreIdsToRemove = genreIds.ToList();
            var novelGenresToRemove = await dbContext.NovelGenres.Where(ng => ng.NovelId == novelId && genreIdsToRemove.Contains(ng.GenreId)).ToListAsync();
            if (novelGenresToRemove.Count == 0) return true;
            dbContext.NovelGenres.RemoveRange(novelGenresToRemove);
            var result = await dbContext.SaveChangesAsync();
            return result > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> UpdateNovelGenres(Guid novelId, IEnumerable<int> genreIds)
    {
        try
        {
            var genreIdsList = genreIds.ToList();
            if (genreIdsList.Count == 0 || genreIdsList.Count > 4) return false;
            var validGenres = await dbContext.Genres.Where(g => genreIdsList.Contains(g.Id)).Select(g => g.Id).ToListAsync();
            if (validGenres.Count != genreIdsList.Count) return false;

            //getting the current NovelGenre Relationships
            var currentNovelGenres = await dbContext.NovelGenres.Where(ng => ng.NovelId == novelId).ToListAsync();
            if (currentNovelGenres.Count > 0) 
            {
                dbContext.NovelGenres.RemoveRange(currentNovelGenres);
            }
            var newNovelGenres = genreIdsList.Select(genreId => new NovelGenre
            {
                NovelId = novelId,
                GenreId = genreId,
                AddedAt = DateTime.UtcNow
            });
            await dbContext.NovelGenres.AddRangeAsync(newNovelGenres);
            var result = await dbContext.SaveChangesAsync();
            return result >= 0;
        }
        catch
        {
            return false;
        }
    }
}
