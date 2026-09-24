using Application.Novels.DTOS;
using Application.Rankings.Queries.GetGenreRanking;
using Application.Rankings.Queries.GetSiteWideRanking;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Ranking;
using Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class RankingQueryHandlerTests
{
    private readonly IRankingRepository rankings = Substitute.For<IRankingRepository>();
    private readonly IGenresRepository genres = Substitute.For<IGenresRepository>();
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();
    private readonly IMapper mapper = Substitute.For<IMapper>();
    private readonly Genre romance = new() { Id = 1, Name = "Romance", Slug = "romance" };

    private GetGenreRankingQueryHandler GenreHandler() =>
        new(rankings, NullLogger<GetGenreRankingQueryHandler>.Instance, genres, mapper);

    private GetSiteWideRankingQueryHandler SiteWideHandler() =>
        new(rankings, novels, NullLogger<GetSiteWideRankingQueryHandler>.Instance, mapper);

    [Fact]
    public async Task Web_app_top_rated_resolves_to_the_stored_TopRated_list()
    {
        genres.GetBySlug("romance").Returns(romance);
        var list = new RankingList { Id = 7, GenreId = 1, RankingType = RankingTypes.TopRated, Name = "TopRomance", TotalNovels = 3 };
        rankings.GetRankingListByGenreAndType(1, RankingTypes.TopRated).Returns(list);
        rankings.GetRankingEntriesPaged(7, 10, 1).Returns([]);
        mapper.Map<IEnumerable<NovelInRankingDto>>(Arg.Any<object>()).Returns([]);

        var result = await GenreHandler().Handle(new GetGenreRankingQuery("romance", "top_rated", 10, 1), CancellationToken.None);

        Assert.Equal(3, result.TotalItemsCount);
        await rankings.Received(1).GetRankingListByGenreAndType(1, RankingTypes.TopRated);
    }

    [Fact]
    public async Task A_genre_without_a_list_returns_an_empty_page_instead_of_404()
    {
        genres.GetBySlug("romance").Returns(romance);
        rankings.GetRankingListByGenreAndType(1, RankingTypes.New).Returns((RankingList?)null);

        var result = await GenreHandler().Handle(new GetGenreRankingQuery("romance", "new", 10, 1), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalItemsCount);
    }

    [Fact]
    public async Task Unknown_ranking_types_are_still_rejected()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            GenreHandler().Handle(new GetGenreRankingQuery("romance", "bogus", 10, 1), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            SiteWideHandler().Handle(new GetSiteWideRankingQuery("TopRated", 10, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Page_size_is_capped_so_one_request_cannot_pull_everything()
    {
        var list = new RankingList { Id = 3, RankingType = RankingTypes.Trending, Name = "TrendingNow", TotalNovels = 500 };
        rankings.GetSiteWideRankingListByType(RankingTypes.Trending).Returns(list);
        mapper.Map<IEnumerable<NovelInRankingDto>>(Arg.Any<object>()).Returns([]);

        await SiteWideHandler().Handle(new GetSiteWideRankingQuery("trending", 100_000, 1), CancellationToken.None);

        await rankings.Received(1).GetRankingEntriesPaged(3, 100, 1);
    }
}
