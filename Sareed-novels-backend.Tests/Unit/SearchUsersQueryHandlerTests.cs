using Application.Common;
using Application.Search.DTOs;
using Application.Search.Queries.SearchUsers;
using Application.Services;
using Application.Users;
using Domain.Repositories;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class SearchUsersQueryHandlerTests
{
    private readonly IUserSearchService search = Substitute.For<IUserSearchService>();
    private readonly IUserContext userContext = Substitute.For<IUserContext>();
    private readonly IUsersRepository users = Substitute.For<IUsersRepository>();

    private SearchUsersQueryHandler Handler() => new(search, userContext, users);

    private void SearchReturns(params string[] ids) =>
        search.SearchUsersAsync(Arg.Any<SearchUsersRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<UserSearchResult>(
                ids.Select(id => new UserSearchResult { Id = id, UserName = id, DisplayName = id }).ToList(),
                ids.Length, 20, 1));

    [Fact]
    public async Task Guests_get_no_follow_state()
    {
        SearchReturns("a", "b");
        userContext.GetCurrentUser().Returns((CurrentUser?)null);

        var result = await Handler().Handle(new SearchUsersQuery(new SearchUsersRequest { Query = "x" }), CancellationToken.None);

        Assert.All(result.Items, u => Assert.Null(u.IsFollowing));
        await users.DidNotReceiveWithAnyArgs().IsFollowingBulkAsync(default!, default!);
    }

    [Fact]
    public async Task A_signed_in_caller_sees_whom_they_already_follow()
    {
        SearchReturns("a", "b");
        userContext.GetCurrentUser().Returns(new CurrentUser("me", "me@test.local", "me", "Me"));
        users.IsFollowingBulkAsync("me", Arg.Any<IEnumerable<string>>())
            .Returns(new Dictionary<string, bool> { ["a"] = true, ["b"] = false });

        var result = await Handler().Handle(new SearchUsersQuery(new SearchUsersRequest { Query = "x" }), CancellationToken.None);

        Assert.Equal(new bool?[] { true, false }, result.Items.Select(u => u.IsFollowing).ToArray());
        Assert.Equal(2, result.TotalItemsCount);
    }

    [Fact]
    public async Task No_follow_lookup_when_nothing_was_found()
    {
        SearchReturns();
        userContext.GetCurrentUser().Returns(new CurrentUser("me", "me@test.local", "me", "Me"));

        var result = await Handler().Handle(new SearchUsersQuery(new SearchUsersRequest { Query = "x" }), CancellationToken.None);

        Assert.Empty(result.Items);
        await users.DidNotReceiveWithAnyArgs().IsFollowingBulkAsync(default!, default!);
    }
}
