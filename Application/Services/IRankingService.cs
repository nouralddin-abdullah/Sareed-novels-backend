namespace Application.Services;

public interface IRankingService
{
    Task CalculateGenreRankings(int genreId, string rankingType = "TopRated");
    Task CalculateAllGenreRankings();
}
