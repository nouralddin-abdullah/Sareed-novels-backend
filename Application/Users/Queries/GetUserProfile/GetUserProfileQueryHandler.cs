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

        return profile;
    }
}
