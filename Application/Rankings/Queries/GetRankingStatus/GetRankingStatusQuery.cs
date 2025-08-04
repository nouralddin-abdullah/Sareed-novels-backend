using MediatR;

namespace Application.Rankings.Queries.GetRankingStatus;

public class GetRankingStatusQuery : IRequest<GetRankingStatusResult>
{

}

public class GetRankingStatusResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;
    public DateTime Timestamp { get; set; }
    public int TotalRankingLists { get; set; }
    public List<RankingListStatus> RankingLists { get; set; } = new();
}

public class RankingListStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public int? GenreId { get; set; }
    public string RankingType { get; set; } = default!;
    public DateTime LastUpdated { get; set; }
    public int TotalNovels { get; set; }
}