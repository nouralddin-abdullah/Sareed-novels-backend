using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CompetitionParticipantRepository(ApplicationDbContext dbContext) : ICompetitionParticipantRepository
{
    public async Task<CompetitionParticipant?> GetByIdAsync(Guid id)
    {
        return await dbContext.CompetitionParticipants
            .Include(p => p.Novel)
                .ThenInclude(n => n.Owner)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<CompetitionParticipant?> GetByCompetitionAndNovelAsync(Guid competitionId, Guid novelId)
    {
        return await dbContext.CompetitionParticipants
            .Include(p => p.Novel)
            .FirstOrDefaultAsync(p => p.CompetitionId == competitionId && p.NovelId == novelId);
    }

    public async Task CreateAsync(CompetitionParticipant participant)
    {
        await dbContext.CompetitionParticipants.AddAsync(participant);
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(CompetitionParticipant participant)
    {
        dbContext.CompetitionParticipants.Update(participant);
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(CompetitionParticipant participant)
    {
        dbContext.CompetitionParticipants.Remove(participant);
        await dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdAsync(Guid competitionId)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.CompetitionId == competitionId)
            .Include(p => p.Novel)
                .ThenInclude(n => n.Owner)
            .Include(p => p.Novel)
                .ThenInclude(n => n.NovelGenres)
                    .ThenInclude(ng => ng.Genre)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdOrderedByNewestAsync(Guid competitionId, int page, int pageSize)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.CompetitionId == competitionId)
            .Include(p => p.Novel)
                .ThenInclude(n => n.Owner)
            .Include(p => p.Novel)
                .ThenInclude(n => n.NovelGenres)
                    .ThenInclude(ng => ng.Genre)
            .OrderByDescending(p => p.JoinedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdOrderedByPointsAsync(Guid competitionId, int page, int pageSize)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.CompetitionId == competitionId)
            .Include(p => p.Novel)
                .ThenInclude(n => n.Owner)
            .Include(p => p.Novel)
                .ThenInclude(n => n.NovelGenres)
                    .ThenInclude(ng => ng.Genre)
            .OrderByDescending(p => p.CurrentPoints + p.ExtraPoints)
            .ThenByDescending(p => p.Novel.TotalViews - p.ViewsAtJoin)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetByNovelIdAsync(Guid novelId)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.NovelId == novelId)
            .Include(p => p.Competition)
            .ToListAsync();
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetByAuthorIdAsync(string authorId)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.Novel.AuthorId == authorId)
            .Include(p => p.Competition)
            .Include(p => p.Novel)
            .ToListAsync();
    }

    public async Task<int> GetParticipantCountAsync(Guid competitionId)
    {
        return await dbContext.CompetitionParticipants
            .CountAsync(p => p.CompetitionId == competitionId);
    }

    public async Task<bool> IsNovelParticipatingAsync(Guid competitionId, Guid novelId)
    {
        return await dbContext.CompetitionParticipants
            .AnyAsync(p => p.CompetitionId == competitionId && p.NovelId == novelId);
    }

    public async Task<IEnumerable<CompetitionParticipant>> GetTopParticipantsAsync(Guid competitionId, int count)
    {
        return await dbContext.CompetitionParticipants
            .Where(p => p.CompetitionId == competitionId)
            .Include(p => p.Novel)
                .ThenInclude(n => n.Owner)
            .OrderByDescending(p => p.CurrentPoints + p.ExtraPoints)
            .ThenByDescending(p => p.Novel.TotalViews - p.ViewsAtJoin)
            .Take(count)
            .ToListAsync();
    }

    public async Task UpdateRankingsAsync(Guid competitionId, IEnumerable<CompetitionParticipant> rankedParticipants)
    {
        dbContext.CompetitionParticipants.UpdateRange(rankedParticipants);
        await dbContext.SaveChangesAsync();
    }
}
