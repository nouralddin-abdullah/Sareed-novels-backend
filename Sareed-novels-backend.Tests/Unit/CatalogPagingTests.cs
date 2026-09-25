using Application.Novels.Queries.GetAllNovels;
using Domain.Entities;
using Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class CatalogPagingTests
{
    private readonly INovelsRepository novels = Substitute.For<INovelsRepository>();

    [Theory]
    [InlineData(0, 0, 1, 1)]            // pageNumber 0 used to become a negative SQL OFFSET (500)
    [InlineData(-4, 50, 1, 50)]
    [InlineData(2, 100_000, 2, 1000)]   // one request can't pull an unbounded page
    [InlineData(3, 100, 3, 100)]
    public async Task All_novels_paging_is_clamped(int pageNumber, int pageSize, int expectedPage, int expectedSize)
    {
        novels.GetAllNovelsBasicAsync(default, default).ReturnsForAnyArgs((Enumerable.Empty<Novel>(), 0));

        var result = await new GetAllNovelsQueryHandler(NullLogger<GetAllNovelsQueryHandler>.Instance, novels)
            .Handle(new GetAllNovelsQuery { PageNumber = pageNumber, PageSize = pageSize }, CancellationToken.None);

        await novels.Received(1).GetAllNovelsBasicAsync(expectedPage, expectedSize);
        Assert.Equal(0, result.TotalPages);
    }
}
