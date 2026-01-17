using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories;

public class CompetitionRepository(ApplicationDbContext dbContext) : ICompetitionRepository
{
    public async Task<Competition?> GetByIdAsync(Guid id)
    {
        return await dbContext.Competitions.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Competition?> GetBySlugAsync(string slug)
    {
        return await dbContext.Competitions.FirstOrDefaultAsync(c => c.Slug == slug);
    }

    public async Task<Competition?> GetByIdWithParticipantsAsync(Guid id)
    {
        return await dbContext.Competitions
            .Include(c => c.Participants)
                .ThenInclude(p => p.Novel)
                    .ThenInclude(n => n.Owner)
            .Include(c => c.Winners)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task CreateAsync(Competition competition)
    {
        await dbContext.Competitions.AddAsync(competition);
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateAsync(Competition competition)
    {
        competition.UpdatedAt = DateTime.UtcNow;
        dbContext.Competitions.Update(competition);
        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Competition competition)
    {
        dbContext.Competitions.Remove(competition);
        await dbContext.SaveChangesAsync();
    }

    public async Task<IEnumerable<Competition>> GetAllAsync()
    {
        return await dbContext.Competitions
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Competition>> GetByStatusAsync(string status)
    {
        return await dbContext.Competitions
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Competition>> GetActiveCompetitionsAsync()
    {
        return await dbContext.Competitions
            .Where(c => c.IsActive && c.Status != CompetitionStatus.Completed)
            .OrderByDescending(c => c.ParticipationStartDate)
            .ToListAsync();
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await dbContext.Competitions.AnyAsync(c => c.Id == id);
    }

    public async Task<bool> SlugExistsAsync(string slug)
    {
        return await dbContext.Competitions.AnyAsync(c => c.Slug == slug);
    }
}
