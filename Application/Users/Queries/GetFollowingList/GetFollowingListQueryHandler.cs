using Application.Common;
using Application.Users.DTOS;
using Application.Users;
using Application.Users.Queries.GetFollowersList;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetFollowingList;

public class GetFollowingListQueryHandler(
    ILogger<GetFollowersListQueryHandler> logger,
    IUsersRepository usersRepository,
    IUserContext userContext,
    UserManager<User> userManager) : IRequestHandler<GetFollowingListQuery, PagedResult<FollowedDto>>
{
    public async Task<PagedResult<FollowedDto>> Handle(GetFollowingListQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("This user is not found");
        logger.LogInformation("Getting following list for user {username}", user.DisplayName);
        
        var (following, totalCount) = await usersRepository.GetFollowingList(user.Id, request.PageSize, request.PageNumber);
        var followingList = following.Select(f => new FollowedDto
        {
            UserId = f.Followed.Id,
            UserName = f.Followed.UserName!,
            DisplayName = f.Followed.DisplayName,
            ProfilePhoto = f.Followed.ProfilePhoto
        }).ToList();
        
        // Bulk check isFollowing if current user exists
        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null && followingList.Any())
        {
            var userIds = followingList.Select(f => f.UserId).ToList();
            var followingMap = await usersRepository.IsFollowingBulkAsync(currentUser.Id, userIds);
            
            foreach (var followed in followingList)
            {
                followed.IsFollowing = followingMap.GetValueOrDefault(followed.UserId, false);
            }
        }
        
        var result = new PagedResult<FollowedDto>(followingList, totalCount, request.PageSize, request.PageNumber);
        return result;
    }
}
