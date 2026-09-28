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
    IUserBlocksRepository blocksRepository,
    IUserContext userContext) : IRequestHandler<GetReadingListDetailQuery, ReadingListDetailDTO>
{
    public async Task<ReadingListDetailDTO> Handle(GetReadingListDetailQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser();
        logger.LogInformation("Getting details for reading list {ListId}", request.ReadingListId);

        var readingList = await readingListsRepository.GetByIdWithDetailsAsync(request.ReadingListId)
            ?? throw new NotFoundException(ReadingListBlocks.NotFoundMessage, ReadingListBlocks.NotFoundCode);

        // As the owner's profile and lists: to someone the owner blocked, the list doesn't exist (the same 404 as a
        // missing list, before the private check, which would tell them it is there).
        await ReadingListBlocks.EnsureNotBlockedByOwnerAsync(blocksRepository, readingList.UserId, currentUser?.Id, cancellationToken);

        if (!readingList.IsPublic && (currentUser == null || readingList.UserId != currentUser.Id))
        {
            throw new ForbidException("هذه القائمة خاصة", "ReadingListPrivate");
        }

        var dto = new ReadingListDetailDTO
        {
            Id = readingList.Id,
            Name = readingList.Name,
            Description = readingList.Description,
            CoverImageUrl = readingList.CoverImageUrl,
            IsPublic = readingList.IsPublic,
            // Set below from the novels readers can see (drafts and deleted novels are left out).
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

        dto.NovelsCount = dto.Novels.Count;

        logger.LogInformation("Returned reading list {ListId} with {NovelCount} novels", readingList.Id, dto.Novels.Count);

        return dto;
    }
}
