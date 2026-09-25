using Domain.Ranking;

namespace Sareed_novels_backend.Tests.Unit;

public class RankingTypesTests
{
    [Theory]
    [InlineData("top_rated", RankingTypes.TopRated)]   // what the web app sends
    [InlineData("TopRated", RankingTypes.TopRated)]
    [InlineData("top-rated", RankingTypes.TopRated)]
    [InlineData("trending", RankingTypes.Trending)]
    [InlineData("Trending", RankingTypes.Trending)]
    [InlineData("new", RankingTypes.New)]
    [InlineData("AllTime", RankingTypes.AllTime)]
    [InlineData("all_time", RankingTypes.AllTime)]
    [InlineData("NewArrivals", RankingTypes.NewArrivals)]
    [InlineData("new-arrivals", RankingTypes.NewArrivals)]
    public void Accepts_the_names_clients_send(string input, string expected) =>
        Assert.Equal(expected, RankingTypes.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("'; DROP TABLE Novels;--")]
    public void Rejects_unknown_types(string? input) => Assert.Null(RankingTypes.Normalize(input));
}
