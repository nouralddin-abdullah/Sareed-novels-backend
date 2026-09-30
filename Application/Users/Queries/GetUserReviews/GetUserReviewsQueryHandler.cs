using Application.Common;
using Application.Users.DTOS;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetUserReviews;

public class GetUserReviewsQueryHandler(
    ILogger<GetUserReviewsQueryHandler> logger,
    UserManager<User> userManager,
    IUsersRepository usersRepository,
    IUserBlocksRepository blocksRepository,
    IProfileListsRepository profileLists,
    IReviewLikesRepository reviewLikesRepository,
    IUserContext userContext) : IRequestHandler<GetUserReviewsQuery, PagedResult<ProfileReviewDTO>>
{
    public async Task<PagedResult<ProfileReviewDTO>> Handle(GetUserReviewsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        // The member the profile shows, or its 404.
        var member = await ProfileLookup.FindMemberAsync(userManager, usersRepository, request.UserName, cancellationToken);

        // As the member's posts: empty, not refused, when the viewer and the member blocked each other.
        var viewer = userContext.GetCurrentUser();
        if (await Blocks.EitherWayAsync(blocksRepository, viewer?.Id, member.Id, cancellationToken))
        {
            return new PagedResult<ProfileReviewDTO>([], 0, pageSize, pageNumber);
        }

        var (reviews, totalCount) = await profileLists.GetReviewsAsync(member.Id, pageNumber, pageSize, cancellationToken);
        var liked = viewer != null && reviews.Count > 0
            ? await reviewLikesRepository.GetUserLikedReviewIds(viewer.Id, reviews.Select(r => r.Id))
            : [];

        logger.LogInformation("Listed {Count} of {Total} reviews of user {UserId}", reviews.Count, totalCount, member.Id);
        return new PagedResult<ProfileReviewDTO>(
            reviews.Select(r => r.ToDto(liked.Contains(r.Id))).ToList(), totalCount, pageSize, pageNumber);
    }
}
