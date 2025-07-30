using Application.Users.DTOS;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetUserProfile;

public class GetUserProfileQueryHandler(ILogger<GetUserProfileQueryHandler> logger, UserManager<User> userManager, IUserContext userContext,IMapper mapper, IUsersRepository usersRepository) : IRequestHandler<GetUserProfileQuery, UserProfile>
{
    public async Task<UserProfile> Handle(GetUserProfileQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? null;
        var user = await userManager.FindByNameAsync(request.UserName) ?? throw new NotFoundException("User is not found");
        logger.LogInformation("Getting profile for {@user}", user);
        var recentFollowers = await usersRepository.GetRecentFollowers(user, 7);
        var recentFollowing = await usersRepository.GetRecentFollowing(user, 7);
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

        //Map to DTOs
        var profile = mapper.Map<UserProfile>(user);
        profile.RecentFollowers = recentFollowers.Select(f => new FollowerDto
        {
            UserId = f.Follower.Id,
            DisplayName = f.Follower.DisplayName,
            UserName = f.Follower.UserName!,
            ProfilePhoto = f.Follower.ProfilePhoto
        }).ToList();


        profile.RecentFollowing = recentFollowing.Select(f => new FollowerDto
        {
            UserId = f.Followed.Id,
            DisplayName = f.Followed.DisplayName,
            UserName = f.Followed.UserName!,
            ProfilePhoto = f.Followed.ProfilePhoto
        }).ToList();
        profile.TotalFollowers = totalFollowers;
        profile.TotalFollowing = totalFollowing;
        profile.IsFollowing = isFollowing;
        //End following System

        return profile;
    }
}
