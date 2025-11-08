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
        logger.LogInformation("Getting followed reading lists for user {UserId}, page {Page}", currentUser.Id, request.PageNumber);

        var (lists, totalCount) = await readingListsRepository.GetFollowedReadingListsWithPreviewAsync(
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
            IsOwner = list.UserId == currentUser.Id,
            IsFollowing = true
        }).ToList();

        return new PagedResult<ReadingListPreviewDTO>(
            dtos,
            totalCount,
            request.PageSize,
            request.PageNumber
        );
    }
}
