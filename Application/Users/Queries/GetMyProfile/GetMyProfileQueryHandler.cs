using Application.Users.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetMyProfile;

public class GetMyProfileQueryHandler(ILogger<GetMyProfileQueryHandler> logger, IUserContext userContext, UserManager<User> userManager, IMapper mapper, IUsersRepository usersRepository, IProfileListsRepository profileLists) : IRequestHandler<GetMyProfileQuery, UserIsProfile>
{
    public async Task<UserIsProfile> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Getting self profile for {UserId}", currentUser.Id);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");


        // Only get total counts (no recent followers/following)
        var totalFollowers = await usersRepository.GetFollowersCount(user);
        var totalFollowing = await usersRepository.GetFollowingCount(user);

        // As on the profile others see: what the member's review and comment lists hold as anyone sees them (#54).
        var counts = await profileLists.CountAsync(user.Id, cancellationToken);

        // Map to DTO
        var profile = mapper.Map<UserIsProfile>(user);
        profile.TotalFollowers = totalFollowers;
        profile.TotalFollowing = totalFollowing;
        profile.ReviewsCount = counts.Reviews;
        profile.CommentsCount = counts.Comments;

        return profile;
    }
}
