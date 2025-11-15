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


        // Only get total counts (no recent followers/following)
        var totalFollowers = await usersRepository.GetFollowersCount(user);
        var totalFollowing = await usersRepository.GetFollowingCount(user);

        // Map to DTO
        var profile = mapper.Map<UserIsProfile>(user);
        profile.TotalFollowers = totalFollowers;
        profile.TotalFollowing = totalFollowing;

        return profile;
    }
}
