using Domain.Entities;

namespace Domain.Repositories;

public interface ICompetitionRepository
{
    // CRUD
    Task<Competition?> GetByIdAsync(Guid id);
    Task<Competition?> GetBySlugAsync(string slug);
    Task<Competition?> GetByIdWithParticipantsAsync(Guid id);
    Task CreateAsync(Competition competition);
    Task UpdateAsync(Competition competition);
    Task DeleteAsync(Competition competition);
    
    // Queries
    Task<IEnumerable<Competition>> GetAllAsync();
    Task<IEnumerable<Competition>> GetByStatusAsync(string status);
    Task<IEnumerable<Competition>> GetActiveCompetitionsAsync();
    Task<bool> ExistsAsync(Guid id);
    Task<bool> SlugExistsAsync(string slug);
}
