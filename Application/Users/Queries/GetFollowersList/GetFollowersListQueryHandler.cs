using Application.Common;
using Application.Users.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetFollowersList;

public class GetFollowersListQueryHandler(
    ILogger<GetFollowersListQueryHandler> logger,
    IUsersRepository usersRepository, 
    IUserContext userContext,
    UserManager<User> userManager) : IRequestHandler<GetFollowersListQuery, PagedResult<FollowerDto>>
{
    public async Task<PagedResult<FollowerDto>> Handle(GetFollowersListQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("This user is not found");
        logger.LogInformation("Getting followers list for user {username}", user.DisplayName);
        
        var (followers, totalCount) = await usersRepository.GetFollowersList(user.Id, request.PageSize, request.PageNumber);
        var followersList = followers.Select(f => new FollowerDto
        {
            UserId = f.Follower.Id,
            UserName = f.Follower.UserName!,
            DisplayName = f.Follower.DisplayName,
            ProfilePhoto = f.Follower.ProfilePhoto
        }).ToList();
        
        // Bulk check isFollowing if current user exists
        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null && followersList.Any())
        {
            var userIds = followersList.Select(f => f.UserId).ToList();
            var followingMap = await usersRepository.IsFollowingBulkAsync(currentUser.Id, userIds);
            
            foreach (var follower in followersList)
            {
                follower.IsFollowing = followingMap.GetValueOrDefault(follower.UserId, false);
            }
        }
        
        var result = new PagedResult<FollowerDto>(followersList, totalCount, request.PageSize, request.PageNumber);
        return result;
    }
}
