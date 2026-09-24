using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Domain.Search;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services.Search;

/// <summary>
/// User search straight from SQL over User.SearchName (normalized display name + user name).
/// Relevance: exact user name > name starts with the query > a word starts with it > contains it, then followers.
/// </summary>
public class UserSearchService(ApplicationDbContext dbContext) : IUserSearchService
{
    public async Task<PagedResult<UserSearchResult>> SearchUsersAsync(
        SearchUsersRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, NovelSearchService.MaxPageSize);
        var tokens = SearchText.Tokens(request.Query);
        var phrase = string.Join(' ', tokens);
        var wordStart = " " + phrase;
        var rawQuery = request.Query?.Trim() ?? string.Empty;

        var query = dbContext.Users.AsNoTracking();
        foreach (var token in tokens)
        {
            query = query.Where(u => u.SearchName.Contains(token));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = tokens.Count == 0
            ? query.OrderByDescending(u => u.Followers.Count)
            : query
                .OrderBy(u => u.UserName == rawQuery ? 0
                    : u.SearchName.StartsWith(phrase) ? 1
                    : u.SearchName.Contains(wordStart) ? 2
                    : 3)
                .ThenByDescending(u => u.Followers.Count);

        var items = await ordered
            .ThenBy(u => u.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserSearchResult
            {
                Id = u.Id,
                UserName = u.UserName!,
                DisplayName = u.DisplayName,
                ProfilePhoto = u.ProfilePhoto,
                FollowersCount = u.Followers.Count,
                FollowingCount = u.Following.Count,
                NovelsCount = u.Novels.Count(n => !n.IsDraft)
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<UserSearchResult>(items, totalCount, pageSize, pageNumber);
    }
}
