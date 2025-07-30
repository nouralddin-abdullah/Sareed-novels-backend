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

public class GetMyProfileQueryHandler(ILogger<GetMyProfileQueryHandler> logger, IUserContext userContext, UserManager<User> userManager, IMapper mapper, IUsersRepository usersRepository) : IRequestHandler<GetMyProfileQuery, UserIsProfile>
{
    public async Task<UserIsProfile> Handle(GetMyProfileQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User is not authenticated");
        logger.LogInformation("Getting self profile for {@user}", currentUser);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("User is not found");

        //Start Following System
        var recentFollowers = await usersRepository.GetRecentFollowers(user, 7);
        var recentFollowing = await usersRepository.GetRecentFollowing(user, 7);
        var totalFollowers = await usersRepository.GetFollowersCount(user);
        var totalFollowing = await usersRepository.GetFollowingCount(user);

        //Map to DTOs
        var profile = mapper.Map<UserIsProfile>(user);
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
        //End following System
        profile.TotalFollowers = totalFollowers;
        profile.TotalFollowing = totalFollowing;


        return profile;

    }
}
