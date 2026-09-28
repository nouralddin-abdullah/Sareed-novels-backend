using Application.Users.DTOS;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetUserProfile;

public class GetUserProfileQueryHandler(ILogger<GetUserProfileQueryHandler> logger, UserManager<User> userManager, IUserContext userContext,IMapper mapper, IUsersRepository usersRepository, IUserBlocksRepository blocksRepository) : IRequestHandler<GetUserProfileQuery, UserProfile>
{
    public async Task<UserProfile> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? null;
        // A name the member used before still finds them (old links); the profile carries the current userName, so
        // clients can move to it. A live user with that name always wins. A deleted account has no profile.
        var user = NotDeleted(await userManager.FindByNameAsync(request.UserName))
            ?? await usersRepository.GetByPreviousUserNameAsync(request.UserName, cancellationToken)
            ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
        logger.LogInformation("Getting profile for {UserId}", user.Id);

        // Someone this user blocked finds no such user (the same answer as for a name nobody has); someone who blocked
        // this user still sees them, flagged, so they can unblock.
        var blockedByMe = false;
        if (currentUser != null && currentUser.Id != user.Id)
        {
            var relation = await blocksRepository.GetRelationAsync(currentUser.Id, user.Id, cancellationToken);
            if (relation.OtherBlockedViewer)
            {
                throw new NotFoundException("المستخدم غير موجود", "UserNotFound");
            }
            blockedByMe = relation.ViewerBlockedOther;
        }
        
        // Only get total counts (no recent followers/following)
        var totalFollowers = await usersRepository.GetFollowersCount(user);
        var totalFollowing = await usersRepository.GetFollowingCount(user);
        
        bool isFollowing;
        if (currentUser != null)
        {
            isFollowing = await usersRepository.IsFollowingAsync(currentUser.Id, user.Id);
        }
        else
        {
            isFollowing = false;
        }

        // Map to DTO
        var profile = mapper.Map<UserProfile>(user);
        profile.TotalFollowers = totalFollowers;
        profile.TotalFollowing = totalFollowing;
        profile.IsFollowing = isFollowing;
        profile.IsBlockedByMe = blockedByMe;

        return profile;
    }

    private static User? NotDeleted(User? user) => user?.DeletedAt == null ? user : null;
}
