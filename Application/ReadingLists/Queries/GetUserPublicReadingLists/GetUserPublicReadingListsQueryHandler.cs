using Application.Common;
using Application.ReadingLists.DTOs;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Queries.GetUserPublicReadingLists;

public class GetUserPublicReadingListsQueryHandler(
    ILogger<GetUserPublicReadingListsQueryHandler> logger,
    IReadingListsRepository readingListsRepository,
    UserManager<User> userManager,
    IUsersRepository usersRepository) : IRequestHandler<GetUserPublicReadingListsQuery, PagedResult<ReadingListPreviewDTO>>
{
    public async Task<PagedResult<ReadingListPreviewDTO>> Handle(GetUserPublicReadingListsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = ReadingListPreviewMapping.ClampPage(request.PageNumber, request.PageSize);
        logger.LogInformation("Getting public reading lists for user {UserName}, page {Page}", request.UserName, pageNumber);

        // Old profile links: a name the member used before still finds them (a live user with it always wins).
        var user = await userManager.FindByNameAsync(request.UserName)
            ?? await usersRepository.GetByPreviousUserNameAsync(request.UserName, cancellationToken)
            ?? throw new NotFoundException("User not found");

        var (lists, totalCount) = await readingListsRepository.GetUserPublicReadingListsWithPreviewAsync(
            user.Id,
            pageNumber,
            pageSize
        );

        var dtos = lists.Select(list => list.ToPreviewDto(isOwner: false, isFollowing: false)).ToList();

        return new PagedResult<ReadingListPreviewDTO>(dtos, totalCount, pageSize, pageNumber);
    }
}
