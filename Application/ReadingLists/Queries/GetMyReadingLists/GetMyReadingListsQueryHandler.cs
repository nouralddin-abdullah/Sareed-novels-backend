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
        logger.LogInformation("Getting reading lists for user {UserId}, page {Page}", currentUser.Id, request.PageNumber);

        var (lists, totalCount) = await readingListsRepository.GetUserReadingListsWithPreviewAsync(
            currentUser.Id,
            request.PageNumber,
            request.PageSize
        );

        var dtos = lists.Select(list => new ReadingListPreviewDTO
        {
            Id = list.Id,
            Name = list.Name,
            Description = list.Description,
            CoverImageUrl = list.CoverImageUrl,
            IsPublic = list.IsPublic,
            NovelsCount = list.NovelsCount,
            FollowersCount = list.FollowersCount,
            UpdatedAt = list.UpdatedAt,
            PreviewNovels = list.Novels
                .Take(5)
                .Select(rln => new NovelPreviewDTO
                {
                    NovelId = rln.Novel.Id,
                    Slug = rln.Novel.Slug,
                    CoverImageUrl = rln.Novel.CoverImageUrl,
                    Title = rln.Novel.Title
                })
                .ToList(),
            IsOwner = true,
            IsFollowing = false
        }).ToList();

        return new PagedResult<ReadingListPreviewDTO>(
            dtos,
            totalCount,
            request.PageSize,
            request.PageNumber
        );
    }
}
