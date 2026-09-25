using Domain.Constants;
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

    public async Task<(IEnumerable<Novel>, int)> GetNovelsByGenre(
        int genreId,
        int pageSize,
        int pageNumber,
        string? sorting,
        bool? isCompleted)
    {
        // Novels without a published chapter yet are listed (the owner's call); drafts are not.
        var query = dbContext.Novels
            .AsNoTracking()
            .Where(n => n.NovelGenres.Any(ng => ng.GenreId == genreId)
                        && n.IsEligibleForRanking
                        && !n.IsDraft);

        if (isCompleted.HasValue)
        {
            var completed = NovelStatus.Completed.ToString();
            query = isCompleted.Value
                ? query.Where(n => n.Status == completed)
                : query.Where(n => n.Status != completed);
        }

        var totalCount = await query.CountAsync();

        // Every order ends on Id so pages never repeat or skip novels that tie (most have 0 reviews / equal scores).
        var ordered = sorting switch
        {
            "newest" => query.OrderByDescending(n => n.CreatedAt),
            "rating" => query.OrderByDescending(n => n.TotalAverageScore).ThenByDescending(n => n.ReviewCount),
            "most_reviewed" => query.OrderByDescending(n => n.ReviewCount).ThenByDescending(n => n.TotalAverageScore),
            _ => query.OrderByDescending(n => n.TotalViews) // "popular" = most viewed
        };

        var novels = await ordered
            .ThenBy(n => n.Id)
            .Skip(pageSize * (pageNumber - 1))
            .Take(pageSize)
            .Include(n => n.NovelGenres)
                .ThenInclude(ng => ng.Genre)
            .ToListAsync();

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
            if (currentNovelGenres.Any()) 
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
