using Application.Common;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Users.Queries.GetBlockedUsers;

/// <summary>GET /api/User/blocked: the users the signed-in user blocked, most recent first, with their current names.</summary>
public class GetBlockedUsersQuery(int pageNumber, int pageSize) : IRequest<PagedResult<BlockedUserDto>>
{
    public int PageNumber { get; } = pageNumber;
    public int PageSize { get; } = pageSize;
}

public class BlockedUserDto
{
    public string UserId { get; set; } = default!;
    public string UserName { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? ProfilePhoto { get; set; }
    public DateTime BlockedAt { get; set; }
}

public class GetBlockedUsersQueryHandler(
    IUserContext userContext,
    IUserBlocksRepository blocksRepository) : IRequestHandler<GetBlockedUsersQuery, PagedResult<BlockedUserDto>>
{
    public async Task<PagedResult<BlockedUserDto>> Handle(GetBlockedUsersQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);

        var (users, totalCount) = await blocksRepository.GetBlockedUsersAsync(currentUser.Id, pageNumber, pageSize, cancellationToken);
        var items = users.Select(u => new BlockedUserDto
        {
            UserId = u.UserId,
            UserName = u.UserName,
            DisplayName = u.DisplayName,
            ProfilePhoto = u.ProfilePhoto,
            BlockedAt = DateTime.SpecifyKind(u.BlockedAt, DateTimeKind.Utc)
        }).ToList();

        return new PagedResult<BlockedUserDto>(items, totalCount, pageSize, pageNumber);
    }
}
