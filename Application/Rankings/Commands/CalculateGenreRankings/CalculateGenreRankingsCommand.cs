using Application.Rankings.Commands.CalculateAllRankings;
using MediatR;

namespace Application.Rankings.Commands.CalculateGenreRankings;

public class CalculateGenreRankingsCommand(int genreId, string rankingType = "TopRated") : IRequest<CalculateGenreRankingsResult>
{
    public int GenreId { get; set; } = genreId;
    public string RankingType { get; set; } = rankingType;

}

public class CalculateGenreRankingsResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = default!;
    public int GenreId { get; set; }
    public string RankingType { get; set; } = default!;
    public double ExecutionTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Error { get; set; }
}