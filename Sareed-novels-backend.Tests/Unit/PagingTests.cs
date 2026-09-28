using Application.Common;

namespace Sareed_novels_backend.Tests.Unit;

public class PagingTests
{
    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-5, -10, 1, 1)]
    [InlineData(int.MinValue, int.MinValue, 1, 1)]
    [InlineData(3, 20, 3, 20)]
    [InlineData(1, 50, 1, 50)]
    [InlineData(1, 51, 1, Paging.MaxPageSize)]
    [InlineData(2, int.MaxValue, 2, Paging.MaxPageSize)]
    public void Pages_start_at_1_and_hold_1_to_50_items(int pageNumber, int pageSize, int expectedNumber, int expectedSize)
    {
        Assert.Equal((expectedNumber, expectedSize), Paging.Clamp(pageNumber, pageSize));
    }

    [Fact]
    public void An_endpoint_can_allow_bigger_pages()
    {
        Assert.Equal((1, 100), Paging.Clamp(0, 1000, maxPageSize: 100));
        Assert.Equal((4, 80), Paging.Clamp(4, 80, maxPageSize: 100));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(1000)]
    public void A_huge_page_number_never_overflows_the_offset(int pageSize)
    {
        var (pageNumber, size) = Paging.Clamp(int.MaxValue, pageSize, maxPageSize: 1000);

        // The repositories compute (pageNumber - 1) * pageSize: it has to stay a valid, non-negative int.
        var offset = checked((pageNumber - 1) * size);
        Assert.True(offset >= 0);
    }

    [Fact]
    public void A_short_last_page_ends_at_the_last_item()
    {
        // The issue's example: one item on a page of two used to say itemsTo: 2.
        var single = new PagedResult<int>([7], totalCount: 1, pageSize: 2, pageNumber: 1);
        Assert.Equal((1, 1, 1), (single.ItemsFrom, single.ItemsTo, single.TotalPages));

        var last = new PagedResult<int>(Enumerable.Range(21, 5), totalCount: 25, pageSize: 10, pageNumber: 3);
        Assert.Equal((21, 25, 3), (last.ItemsFrom, last.ItemsTo, last.TotalPages));
    }

    [Fact]
    public void A_full_page_and_an_empty_list_count_as_before()
    {
        var full = new PagedResult<int>(Enumerable.Range(11, 10), totalCount: 30, pageSize: 10, pageNumber: 2);
        Assert.Equal((11, 20, 3), (full.ItemsFrom, full.ItemsTo, full.TotalPages));

        var empty = new PagedResult<int>([], totalCount: 0, pageSize: 10, pageNumber: 1);
        Assert.Equal((1, 0, 0), (empty.ItemsFrom, empty.ItemsTo, empty.TotalPages));
    }

    [Fact]
    public void A_page_past_the_end_ends_at_the_last_item_so_infinite_lists_stop()
    {
        // The web's followers lists ask for the next page while ceil(itemsTo / pageSize) < totalPages.
        var past = new PagedResult<int>([], totalCount: 5, pageSize: 10, pageNumber: 3);
        Assert.Equal(5, past.ItemsTo);
        Assert.Equal(past.TotalPages, (int)Math.Ceiling(past.ItemsTo / 10.0));
    }

    [Fact]
    public void A_page_size_of_zero_does_not_divide_by_zero()
    {
        var result = new PagedResult<int>([], totalCount: 3, pageSize: 0, pageNumber: 0);
        Assert.Equal((3, 1, 1), (result.TotalPages, result.ItemsFrom, result.ItemsTo));
    }
}
