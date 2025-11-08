using Application.ReadingLists.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Queries.GetReadingListDetail;

public class GetReadingListDetailQueryHandler(
    ILogger<GetReadingListDetailQueryHandler> logger,
    IReadingListsRepository readingListsRepository,
    IReadingListFollowersRepository followersRepository,
    IUserContext userContext) : IRequestHandler<GetReadingListDetailQuery, ReadingListDetailDTO>
{
    public async Task<ReadingListDetailDTO> Handle(GetReadingListDetailQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser();
        logger.LogInformation("Getting details for reading list {ListId}", request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdWithDetailsAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (!readingList.IsPublic && (currentUser == null || readingList.UserId != currentUser.Id))
        {
            throw new ForbidException("This reading list is private");
        }

        var dto = new ReadingListDetailDTO
        {
            Id = readingList.Id,
            Name = readingList.Name,
            Description = readingList.Description,
            CoverImageUrl = readingList.CoverImageUrl,
            IsPublic = readingList.IsPublic,
            NovelsCount = readingList.NovelsCount,
            FollowersCount = readingList.FollowersCount,
            CreatedAt = readingList.CreatedAt,
            UpdatedAt = readingList.UpdatedAt,
            OwnerUserId = readingList.UserId,
            OwnerUserName = readingList.Owner.UserName!,
            OwnerDisplayName = readingList.Owner.DisplayName,
            OwnerProfilePhoto = readingList.Owner.ProfilePhoto,
            Novels = readingList.Novels
                .Where(rln => !rln.Novel.IsDraft)
                .OrderBy(rln => rln.OrderIndex)
                .Select(rln => new NovelInListDTO
                {
                    NovelId = rln.Novel.Id,
                    Title = rln.Novel.Title,
                    Slug = rln.Novel.Slug,
                    CoverImageUrl = rln.Novel.CoverImageUrl,
                    Summary = rln.Novel.Summary,
                    TotalAverageScore = rln.Novel.TotalAverageScore,
                    ReviewCount = rln.Novel.ReviewCount,
                    Genres = rln.Novel.NovelGenres
                        .Select(ng => ng.Genre.Name)
                        .ToList(),
                    OrderIndex = rln.OrderIndex,
                    AddedAt = rln.AddedAt
                })
                .ToList(),
            IsOwner = currentUser != null && readingList.UserId == currentUser.Id,
            IsFollowing = currentUser != null && await followersRepository.IsFollowingAsync(request.ReadingListId, currentUser.Id)
        };

        logger.LogInformation("Returned reading list {ListId} with {NovelCount} novels", readingList.Id, dto.Novels.Count);

        return dto;
    }
}
