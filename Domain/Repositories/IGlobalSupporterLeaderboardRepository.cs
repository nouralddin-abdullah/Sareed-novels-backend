using Domain.Entities;

namespace Domain.Repositories;

public interface IGlobalSupporterLeaderboardRepository
{
    Task<(IEnumerable<GlobalSupporterLeaderboard> supporters, int totalCount)> GetLeaderboard(string period, int pageNumber, int pageSize);
    Task RecalculateWeeklyLeaderboard();
    Task RecalculateAllTimeLeaderboard();
    Task ClearLeaderboard(string period);
}
