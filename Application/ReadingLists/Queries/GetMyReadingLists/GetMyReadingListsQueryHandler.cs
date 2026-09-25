using Application.Common;
using Application.ReadingLists.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Queries.GetMyReadingLists;

public class GetMyReadingListsQueryHandler(
    ILogger<GetMyReadingListsQueryHandler> logger,
    IReadingListsRepository readingListsRepository,
    IUserContext userContext) : IRequestHandler<GetMyReadingListsQuery, PagedResult<ReadingListPreviewDTO>>
{
    public async Task<PagedResult<ReadingListPreviewDTO>> Handle(GetMyReadingListsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var (pageNumber, pageSize) = ReadingListPreviewMapping.ClampPage(request.PageNumber, request.PageSize);
        logger.LogInformation("Getting reading lists for user {UserId}, page {Page}", currentUser.Id, pageNumber);

        var (lists, totalCount) = await readingListsRepository.GetUserReadingListsWithPreviewAsync(
            currentUser.Id,
            pageNumber,
            pageSize
        );

        var dtos = lists.Select(list => list.ToPreviewDto(isOwner: true, isFollowing: false)).ToList();

        return new PagedResult<ReadingListPreviewDTO>(dtos, totalCount, pageSize, pageNumber);
    }
}
