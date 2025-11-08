using Application.ReadingLists.Commands.AddNovelToList;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.CreateReadingList;

public class CreateReadingListCommandHandler(
    ILogger<CreateReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IUserContext userContext,
    IFileUploadService fileUploadService) : IRequestHandler<CreateReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Creating new reading list for {user}: ", currentUser.UserName);

        if (await readingListsRepository.IsNameTakenByUserAsync(currentUser.Id, request.Name))
        {
            throw new InvalidOperationException($"You already have a reading list named '{request.Name}'");
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
            return new OperationResult
            {
                Success = true,
                Message = "Reading list was created successfully."
            };
        }
        return new OperationResult
        {
            Success = false,
            Message = "Failed to create reading list."
        };
    }
}
