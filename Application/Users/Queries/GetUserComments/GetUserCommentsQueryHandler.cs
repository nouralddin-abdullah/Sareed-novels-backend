using Application.Common;
using Application.Users.DTOS;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetUserComments;

public class GetUserCommentsQueryHandler(
    ILogger<GetUserCommentsQueryHandler> logger,
    UserManager<User> userManager,
    IUsersRepository usersRepository,
    IUserBlocksRepository blocksRepository,
    IProfileListsRepository profileLists,
    ICommentLikesRepository commentLikesRepository,
    IUserContext userContext) : IRequestHandler<GetUserCommentsQuery, PagedResult<ProfileCommentDTO>>
{
    public async Task<PagedResult<ProfileCommentDTO>> Handle(GetUserCommentsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        // The member the profile shows, or its 404.
        var member = await ProfileLookup.FindMemberAsync(userManager, usersRepository, request.UserName, cancellationToken);

        // As the member's posts: empty, not refused, when the viewer and the member blocked each other.
        var viewer = userContext.GetCurrentUser();
        if (await Blocks.EitherWayAsync(blocksRepository, viewer?.Id, member.Id, cancellationToken))
        {
            return new PagedResult<ProfileCommentDTO>([], 0, pageSize, pageNumber);
        }

        var (comments, totalCount) = await profileLists.GetCommentsAsync(member.Id, pageNumber, pageSize, cancellationToken);
        var liked = viewer != null && comments.Count > 0
            ? await commentLikesRepository.GetUserLikedCommentIds(viewer.Id, comments.Select(c => c.Id))
            : [];

        logger.LogInformation("Listed {Count} of {Total} comments of user {UserId}", comments.Count, totalCount, member.Id);
        return new PagedResult<ProfileCommentDTO>(
            comments.Select(c => c.ToDto(liked.Contains(c.Id))).ToList(), totalCount, pageSize, pageNumber);
    }
}
