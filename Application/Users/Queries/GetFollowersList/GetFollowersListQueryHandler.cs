using Application.Common;
using Application.Users.DTOS;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetFollowersList;

public class GetFollowersListQueryHandler(ILogger<GetFollowersListQueryHandler> logger, IMapper mapper, IUsersRepository usersRepository, UserManager<User> userManager) : IRequestHandler<GetFollowersListQuery, PagedResult<FollowerDto>>
{
    public async Task<PagedResult<FollowerDto>> Handle(GetFollowersListQuery request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId) ?? throw new NotFoundException("This user is not found");
        logger.LogInformation("Getting followers list for user {username}", user.DisplayName);
        var (followers, totalCount) = await usersRepository.GetFollowersList(user.Id, request.PageSize, request.PageNumber);
        var followersList = mapper.Map<IEnumerable<FollowerDto>>(followers);
        var result = new PagedResult<FollowerDto>(followersList, totalCount, request.PageSize, request.PageNumber);
        return result;
    }
}
