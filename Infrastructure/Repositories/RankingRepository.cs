using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Repositories;

public class RankingRepository(ApplicationDbContext dbContext) : IRankingRepository
{
    public async Task<IEnumerable<RankingList>> GetAllRankingLists()
    {
        return await dbContext.RankingLists
            .OrderBy(rl => rl.GenreId)
            .ThenBy(rl => rl.RankingType)
            .ToListAsync();
    }

    public async Task<RankingList?> GetRankingListByGenreAndType(int genreId, string rankingType)
    {
        return await dbContext.RankingLists
            .FirstOrDefaultAsync(rl => rl.GenreId == genreId && rl.RankingType == rankingType);
    }

    public async Task<IEnumerable<RankingEntry>> GetRankingEntriesPaged(int rankingListId, int pageSize, int pageNumber)
    {
        var skip = (pageNumber - 1) * pageSize;

        return await dbContext.RankingEntries
            .Where(re => re.RankingListId == rankingListId)
            .Include(re => re.Novel)
                .ThenInclude(n => n.NovelGenres)
                    .ThenInclude(ng => ng.Genre)
            .OrderBy(re => re.Rank)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();
    }
}
