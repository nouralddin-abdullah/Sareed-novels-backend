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
    UserManager<User> userManager) : IRequestHandler<GetUserPublicReadingListsQuery, PagedResult<ReadingListPreviewDTO>>
{
    public async Task<PagedResult<ReadingListPreviewDTO>> Handle(GetUserPublicReadingListsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting public reading lists for user {UserName}, page {Page}", request.UserName, request.PageNumber);

        var user = await userManager.FindByNameAsync(request.UserName)
            ?? throw new NotFoundException("User not found");

        var (lists, totalCount) = await readingListsRepository.GetUserPublicReadingListsWithPreviewAsync(
            user.Id,
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
            IsOwner = false,
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
