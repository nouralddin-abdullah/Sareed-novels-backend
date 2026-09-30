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

    /// <summary>
    /// Competitions whose status at <paramref name="utcNow"/> (<see cref="Competition.EffectiveStatus"/>, decided in SQL)
    /// is <paramref name="status"/>, one of the four as written in <see cref="CompetitionStatus"/>; newest first.
    /// </summary>
    Task<IEnumerable<Competition>> GetByStatusAsync(string status, DateTime utcNow);

    /// <summary>Active competitions not completed at <paramref name="utcNow"/>; latest participation start first.</summary>
    Task<IEnumerable<Competition>> GetActiveCompetitionsAsync(DateTime utcNow);
    Task<bool> ExistsAsync(Guid id);
    Task<bool> SlugExistsAsync(string slug);

    /// <summary>
    /// Write-locks the competition row until the current transaction ends (touches UpdatedAt), so work that must run
    /// once per competition, like finalizing, is serialized. Returns false if the competition doesn't exist.
    /// </summary>
    Task<bool> LockForUpdateAsync(Guid id);
}
