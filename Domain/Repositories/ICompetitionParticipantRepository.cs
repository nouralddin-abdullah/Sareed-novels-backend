using Domain.Entities;

namespace Domain.Repositories;

public interface ICompetitionParticipantRepository
{
    // CRUD
    Task<CompetitionParticipant?> GetByIdAsync(Guid id);
    Task<CompetitionParticipant?> GetByCompetitionAndNovelAsync(Guid competitionId, Guid novelId);
    Task CreateAsync(CompetitionParticipant participant);
    Task UpdateAsync(CompetitionParticipant participant);
    Task DeleteAsync(CompetitionParticipant participant);
    
    // Queries
    Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdAsync(Guid competitionId);
    Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdOrderedByNewestAsync(Guid competitionId, int page, int pageSize);
    Task<IEnumerable<CompetitionParticipant>> GetByCompetitionIdOrderedByPointsAsync(Guid competitionId, int page, int pageSize);
    Task<IEnumerable<CompetitionParticipant>> GetByNovelIdAsync(Guid novelId);
    Task<IEnumerable<CompetitionParticipant>> GetByAuthorIdAsync(string authorId);
    Task<int> GetParticipantCountAsync(Guid competitionId);
    Task<bool> IsNovelParticipatingAsync(Guid competitionId, Guid novelId);
    
    // Ranking
    Task<IEnumerable<CompetitionParticipant>> GetTopParticipantsAsync(Guid competitionId, int count);
    Task UpdateRankingsAsync(Guid competitionId, IEnumerable<CompetitionParticipant> rankedParticipants);
}
