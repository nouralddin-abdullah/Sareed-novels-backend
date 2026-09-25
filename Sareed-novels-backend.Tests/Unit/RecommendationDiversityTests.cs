using Infrastructure.Services.Search;

namespace Sareed_novels_backend.Tests.Unit;

public class RecommendationDiversityTests
{
    private static List<string> Limit(IEnumerable<string> ranked, int max) =>
        NovelRecommendationService.LimitPerAuthor(ranked, item => item[..1], max);

    [Fact]
    public void An_author_past_the_limit_waits_until_everyone_else_has_been_placed()
    {
        var ranked = new[] { "a1", "a2", "a3", "b1", "a4", "c1", "b2" };

        Assert.Equal(new[] { "a1", "a2", "b1", "c1", "b2", "a3", "a4" }, Limit(ranked, 2));
    }

    [Fact]
    public void Keeps_order_and_every_item_when_no_author_is_over_the_limit()
    {
        var ranked = new[] { "a1", "b1", "a2", "c1" };

        Assert.Equal(ranked, Limit(ranked, 2));
        Assert.Equal(new[] { "a1", "b1", "c1", "a2" }, Limit(ranked, 1));
    }

    [Fact]
    public void Handles_an_empty_list()
    {
        Assert.Empty(Limit([], 2));
    }
}
