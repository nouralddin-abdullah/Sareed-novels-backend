using Application.ReadingLists.Commands.AddNovelToList;
using Application.ReadingLists.Queries;
using Application.Services;
using Application.Users;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommandHandler(
    ILogger<CreateReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService) : IRequestHandler<CreateReadingListCommand, CreateReadingListResult>
{
    public async Task<CreateReadingListResult> Handle(CreateReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in", "NotSignedIn");
        logger.LogInformation("Creating new reading list for {user}: ", currentUser.UserName);

        if (await readingListsRepository.IsNameTakenByUserAsync(currentUser.Id, request.Name))
        {
            return new CreateReadingListResult
            {
                Success = false,
                Code = "DuplicateListName",
                Message = $"You already have a reading list named '{request.Name}'"
            };
        }

        if (request.NovelId is { } novelId)
        {
            var refusal = await NovelForReadingList.WhyNotAddable(novelsRepository, novelId);
            if (refusal != null)
            {
                return new CreateReadingListResult { Success = false, Code = NovelForReadingList.NotAddableCode, Message = refusal };
            }
        }

        var readlingList = new ReadingList
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            UserId = currentUser.Id,
            IsPublic = request.IsPublic,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            NovelsCount = 0,
            FollowersCount = 0
        };

        if (request.NovelId is { } firstNovelId)
        {
            // Inserted by the same SaveChanges as the list (one transaction): the list is never saved without it.
            readlingList.Novels.Add(new ReadingListNovel
            {
                ReadingListId = readlingList.Id,
                NovelId = firstNovelId,
                AddedAt = DateTime.UtcNow,
                OrderIndex = 0
            });
            readlingList.NovelsCount = 1;
        }

        if (request.CoverImage != null)
        {
            using var stream = request.CoverImage.OpenReadStream();
            readlingList.CoverImageUrl = await fileUploadService.UploadReadingListCoverImageAsync(
                stream,
                request.CoverImage.ContentType,
                readlingList.Id.ToString()
            );
        }
        var result = await readingListsRepository.CreateAsync(readlingList);
        if (result)
        {
            // Summarized as "my lists" shows it, so the app can put it straight into that list.
            var summary = await readingListsRepository.GetSummaryAsync(readlingList.Id)
                ?? throw new InvalidOperationException($"Reading list {readlingList.Id} was saved but can't be read back");
            return new CreateReadingListResult
            {
                Success = true,
                Message = "Reading list was created successfully.",
                ReadingList = summary.ToPreviewDto(isOwner: true, isFollowing: false)
            };
        }
        return new CreateReadingListResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "Failed to create reading list."
        };
    }
}
