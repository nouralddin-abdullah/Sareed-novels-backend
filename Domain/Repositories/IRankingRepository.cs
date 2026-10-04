using Domain.Entities;

namespace Domain.Repositories;

public interface IRankingRepository
{
    Task<IEnumerable<RankingList>> GetAllRankingLists();
    Task<RankingList?> GetRankingListByGenreAndType(int genreId, string rankingType);
    Task<RankingList?> GetSiteWideRankingListByType(string rankingType);
    /// <summary>
    /// A page of a ranking list in rank order, each entry with its novel, the novel's genres and its author (Owner,
    /// #68).
    /// </summary>
    Task<IEnumerable<RankingEntry>> GetRankingEntriesPaged(int rankingListId, int pageSize, int pageNumber);

    /// <summary>
    /// A page of a ranking list, optionally only completed (true) or only ongoing (false) novels, with the matching
    /// count; loaded as the other overload loads it.
    /// </summary>
    Task<(IEnumerable<RankingEntry> Entries, int TotalCount)> GetRankingEntriesPaged(
        int rankingListId, int pageSize, int pageNumber, bool? isCompleted);
}
