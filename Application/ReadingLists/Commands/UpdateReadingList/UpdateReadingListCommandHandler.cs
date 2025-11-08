using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.ReadingLists.Commands.UpdateReadingList;

public class UpdateReadingListCommandHandler(
    ILogger<UpdateReadingListCommandHandler> logger,
    IReadingListsRepository readingListsRepository,
    IFileUploadService fileUploadService,
    IUserContext userContext) : IRequestHandler<UpdateReadingListCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Updating reading list {ListId} for user {UserId}", request.ReadingListId, currentUser.Id);

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("Reading list not found");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("You don't own this reading list");
        }

        // Check name uniqueness if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != readingList.Name)
        {
            if (await readingListsRepository.IsNameTakenByUserAsync(currentUser.Id, request.Name, request.ReadingListId))
            {
                return new OperationResult
                {
                    Success = false,
                    Message = $"You already have a reading list named '{request.Name}'"
                };
            }
            readingList.Name = request.Name;
        }

        // Update description if provided
        if (request.Description != null)
        {
            readingList.Description = request.Description;
        }

        // Update visibility if provided
        if (request.IsPublic.HasValue)
        {
            readingList.IsPublic = request.IsPublic.Value;
        }

        // Upload cover image if provided
        if (request.CoverImage != null)
        {
            try
            {
                using var stream = request.CoverImage.OpenReadStream();
                readingList.CoverImageUrl = await fileUploadService.UploadReadingListCoverImageAsync(
                    stream,
                    request.CoverImage.ContentType,
                    readingList.Id.ToString()
                );
                logger.LogInformation("Cover image uploaded for reading list {ListId}", readingList.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload cover image for reading list {ListId}", readingList.Id);
                return new OperationResult
                {
                    Success = false,
                    Message = "Failed to upload cover image"
                };
            }
        }

        var result = await readingListsRepository.UpdateAsync(readingList);

        if (result)
        {
            logger.LogInformation("Reading list {ListId} updated successfully", readingList.Id);
            return new OperationResult
            {
                Success = true,
                Message = "Reading list updated successfully"
            };
        }

        return new OperationResult
        {
            Success = false,
            Message = "Failed to update reading list"
        };
    }
}
