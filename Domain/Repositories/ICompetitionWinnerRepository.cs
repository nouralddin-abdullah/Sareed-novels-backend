using Domain.Entities;

namespace Domain.Repositories;

public interface ICompetitionWinnerRepository
{
    Task<CompetitionWinner?> GetByIdAsync(Guid id);
    Task<IEnumerable<CompetitionWinner>> GetByCompetitionIdAsync(Guid competitionId);
    Task<IEnumerable<CompetitionWinner>> GetByAuthorIdAsync(string authorId);
    Task<IEnumerable<CompetitionWinner>> GetByNovelIdAsync(Guid novelId);
    Task CreateAsync(CompetitionWinner winner);
    Task CreateRangeAsync(IEnumerable<CompetitionWinner> winners);
    Task DeleteByCompetitionIdAsync(Guid competitionId);
}
