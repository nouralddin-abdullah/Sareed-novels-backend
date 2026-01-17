using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CompetitionWinnerRepository(ApplicationDbContext dbContext) : ICompetitionWinnerRepository
{
    public async Task<CompetitionWinner?> GetByIdAsync(Guid id)
    {
        return await dbContext.CompetitionWinners
            .Include(w => w.Novel)
            .Include(w => w.Author)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<IEnumerable<CompetitionWinner>> GetByCompetitionIdAsync(Guid competitionId)
    {
        return await dbContext.CompetitionWinners
            .Where(w => w.CompetitionId == competitionId)
            .Include(w => w.Novel)
                .ThenInclude(n => n.Owner)
            .Include(w => w.Author)
            .OrderBy(w => w.Rank)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionWinner>> GetByAuthorIdAsync(string authorId)
    {
        return await dbContext.CompetitionWinners
            .Where(w => w.AuthorId == authorId)
            .Include(w => w.Competition)
            .Include(w => w.Novel)
            .OrderByDescending(w => w.AwardedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionWinner>> GetByNovelIdAsync(Guid novelId)
    {
        return await dbContext.CompetitionWinners
            .Where(w => w.NovelId == novelId)
            .Include(w => w.Competition)
            .OrderByDescending(w => w.AwardedAt)
            .ToListAsync();
    }

    public async Task CreateAsync(CompetitionWinner winner)
    {
        await dbContext.CompetitionWinners.AddAsync(winner);
        await dbContext.SaveChangesAsync();
    }

    public async Task CreateRangeAsync(IEnumerable<CompetitionWinner> winners)
    {
        await dbContext.CompetitionWinners.AddRangeAsync(winners);
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteByCompetitionIdAsync(Guid competitionId)
    {
        var winners = await dbContext.CompetitionWinners
            .Where(w => w.CompetitionId == competitionId)
            .ToListAsync();
        
        dbContext.CompetitionWinners.RemoveRange(winners);
        await dbContext.SaveChangesAsync();
    }
}
