using Application.Common;
using Application.Users.DTOS;
using Application.Users.Queries.GetFollowersList;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetFollowingList;

public class GetFollowingListQueryHandler(ILogger<GetFollowersListQueryHandler> logger, IMapper mapper, IUsersRepository usersRepository, UserManager<User> userManager) : IRequestHandler<GetFollowingListQuery, PagedResult<FollowedDto>>
{
    public async Task<PagedResult<FollowedDto>> Handle(GetFollowingListQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("This user is not found");
        logger.LogInformation("Getting following list for user {username}", user.DisplayName);
        var (following, totalCount) = await usersRepository.GetFollowingList(user.Id, request.PageSize, request.PageNumber);
        var followingList = mapper.Map<IEnumerable<FollowedDto>>(following);
        var result = new PagedResult<FollowedDto>(followingList, totalCount, request.PageSize, request.PageNumber);
        return result;
    }
}
