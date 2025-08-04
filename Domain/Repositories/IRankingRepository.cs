using Domain.Entities;

namespace Domain.Repositories;

public interface IRankingRepository
{
    Task<IEnumerable<RankingList>> GetAllRankingLists();
    Task<RankingList?> GetRankingListByGenreAndType(int genreId, string rankingType);
    Task<IEnumerable<RankingEntry>> GetRankingEntriesPaged(int rankingListId, int pageSize, int pageNumber);
}
