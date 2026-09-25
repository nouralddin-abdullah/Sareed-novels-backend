using Application.Common;
using Application.ReadingLists.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Queries.GetFollowedReadingLists;

public class GetFollowedReadingListsQueryHandler(
    ILogger<GetFollowedReadingListsQueryHandler> logger,
    IReadingListsRepository readingListsRepository,
    IUserContext userContext) : IRequestHandler<GetFollowedReadingListsQuery, PagedResult<ReadingListPreviewDTO>>
{
    public async Task<PagedResult<ReadingListPreviewDTO>> Handle(GetFollowedReadingListsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var (pageNumber, pageSize) = ReadingListPreviewMapping.ClampPage(request.PageNumber, request.PageSize);
        logger.LogInformation("Getting followed reading lists for user {UserId}, page {Page}", currentUser.Id, pageNumber);

        var (lists, totalCount) = await readingListsRepository.GetFollowedReadingListsWithPreviewAsync(
            currentUser.Id,
            pageNumber,
            pageSize
        );

        var dtos = lists.Select(list => list.ToPreviewDto(isOwner: list.List.UserId == currentUser.Id, isFollowing: true)).ToList();

        return new PagedResult<ReadingListPreviewDTO>(dtos, totalCount, pageSize, pageNumber);
    }
}
