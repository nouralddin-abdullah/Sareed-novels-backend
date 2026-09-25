using Domain.Entities;

namespace Domain.Repositories;

public interface IRankingRepository
{
    Task<IEnumerable<RankingList>> GetAllRankingLists();
    Task<RankingList?> GetRankingListByGenreAndType(int genreId, string rankingType);
    Task<RankingList?> GetSiteWideRankingListByType(string rankingType);
    Task<IEnumerable<RankingEntry>> GetRankingEntriesPaged(int rankingListId, int pageSize, int pageNumber);

    /// <summary>A page of a ranking list, optionally only completed (true) or only ongoing (false) novels, with the matching count.</summary>
    Task<(IEnumerable<RankingEntry> Entries, int TotalCount)> GetRankingEntriesPaged(
        int rankingListId, int pageSize, int pageNumber, bool? isCompleted);
}
