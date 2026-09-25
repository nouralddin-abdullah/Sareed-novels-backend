using Application.Gifts.DTOs;
using Application.Gifts.Queries.GetAllGifts;
using Application.Gifts.Queries.GetMyGiftHistory;
using Application.Gifts.Queries.GetNovelGifts;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The gift endpoints passed (pageNumber, pageSize) to a constructor that takes (pageSize, pageNumber), so the live
/// catalogue of 8 gifts reported totalPages 8, itemsFrom 20, itemsTo 20 for pageSize=20.
/// </summary>
public class GiftQueryPagingTests
{
    private readonly IGiftRepository gifts = Substitute.For<IGiftRepository>();
    private readonly IGiftTransactionRepository transactions = Substitute.For<IGiftTransactionRepository>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly IMapper mapper = Substitute.For<IMapper>();

    public GiftQueryPagingTests()
    {
        mapper.Map<List<GiftDto>>(Arg.Any<object>()).Returns([]);
        mapper.Map<List<GiftTransactionDto>>(Arg.Any<object>()).Returns([]);
        userContext.GetCurrentUser().Returns(new CurrentUser("reader-1", "e", "u", "d"));
    }

    [Fact]
    public async Task The_catalogue_of_eight_gifts_is_one_page_of_twenty()
    {
        gifts.GetAllGifts(1, 20, false).Returns((Enumerable.Repeat(new Gift(), 8), 8));

        var page = await new GetAllGiftsQueryHandler(gifts, mapper)
            .Handle(new GetAllGiftsQuery(pageNumber: 1, pageSize: 20), CancellationToken.None);

        Assert.Equal(8, page.TotalItemsCount);
        Assert.Equal(1, page.TotalPages);
        Assert.Equal(1, page.ItemsFrom);
        Assert.Equal(20, page.ItemsTo);
    }

    [Fact]
    public async Task Catalogue_pages_count_and_offset_by_page_size()
    {
        gifts.GetAllGifts(2, 3, false).Returns((Enumerable.Repeat(new Gift(), 3), 8));

        var page = await new GetAllGiftsQueryHandler(gifts, mapper)
            .Handle(new GetAllGiftsQuery(pageNumber: 2, pageSize: 3), CancellationToken.None);

        Assert.Equal(3, page.TotalPages);
        Assert.Equal(4, page.ItemsFrom);
        Assert.Equal(6, page.ItemsTo);
    }

    [Fact]
    public async Task Novel_gifts_page_two_of_forty_five()
    {
        var novelId = Guid.NewGuid();
        transactions.GetTransactionsByNovel(novelId, 2, 20).Returns((Enumerable.Repeat(new GiftTransaction(), 20), 45));

        var page = await new GetNovelGiftsQueryHandler(transactions, mapper)
            .Handle(new GetNovelGiftsQuery(novelId, pageNumber: 2, pageSize: 20), CancellationToken.None);

        Assert.Equal(45, page.TotalItemsCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(21, page.ItemsFrom);
        Assert.Equal(40, page.ItemsTo);
    }

    [Fact]
    public async Task My_gift_history_reads_the_signed_in_user_and_pages_correctly()
    {
        transactions.GetTransactionsBySender("reader-1", 1, 4).Returns((Enumerable.Repeat(new GiftTransaction(), 4), 9));

        var page = await new GetMyGiftHistoryQueryHandler(transactions, userContext, mapper)
            .Handle(new GetMyGiftHistoryQuery(pageNumber: 1, pageSize: 4), CancellationToken.None);

        Assert.Equal(3, page.TotalPages);
        Assert.Equal(1, page.ItemsFrom);
        Assert.Equal(4, page.ItemsTo);
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-3, 20, 1, 20)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 5000, 1, 100)]
    public async Task Out_of_range_paging_is_clamped_instead_of_failing(int pageNumber, int pageSize, int usedPage, int usedSize)
    {
        var novelId = Guid.NewGuid();
        gifts.GetAllGifts(usedPage, usedSize, false).Returns((Enumerable.Empty<Gift>(), 8));
        transactions.GetTransactionsByNovel(novelId, usedPage, usedSize).Returns((Enumerable.Empty<GiftTransaction>(), 0));

        var catalogue = await new GetAllGiftsQueryHandler(gifts, mapper)
            .Handle(new GetAllGiftsQuery(pageNumber, pageSize), CancellationToken.None);
        var novelGifts = await new GetNovelGiftsQueryHandler(transactions, mapper)
            .Handle(new GetNovelGiftsQuery(novelId, pageNumber, pageSize), CancellationToken.None);

        await gifts.Received(1).GetAllGifts(usedPage, usedSize, false);
        await transactions.Received(1).GetTransactionsByNovel(novelId, usedPage, usedSize);
        Assert.Equal(1, catalogue.ItemsFrom);
        Assert.Equal((int)Math.Ceiling(8 / (double)usedSize), catalogue.TotalPages);
        Assert.Equal(0, novelGifts.TotalPages);
    }
}
