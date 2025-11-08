using Application.Services;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Commands.UpdateMe;

public class UpdateMeCommandHandler(
    ILogger<UpdateMeCommandHandler> logger, 
    IFileUploadService fileUploadService, 
    IUserContext userContext, 
    UserManager<User> userManager, 
    IMapper mapper,
    ISearchIndexQueueService searchIndexQueue) : IRequestHandler<UpdateMeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateMeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("The user is not authorized");
        logger.LogInformation("Updating data for user {username}", currentUser.UserName);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("This user was not found");

        //if username is provided and not null test if it was taken before? or available
        if (!string.IsNullOrEmpty(request.UserName) && request.UserName != user.UserName)
        {
            var existingUser = await userManager.FindByNameAsync(request.UserName);
            if (existingUser != null)
            {
                return new OperationResult
                {
                    Success = false,
                    Message = "Username is already taken"
                };
            }
        }

        //if request has ProfilePhoto you should upload it to CloudFlare first then assign it to the url
        string? newProfilePhotoUrl = null;
        if (request.ProfilePhoto != null)
        {
            try
            {
                var stream = request.ProfilePhoto.OpenReadStream();
                newProfilePhotoUrl = await fileUploadService.UploadImageAsync(
                    stream,
                    request.ProfilePhoto.FileName,
                    request.ProfilePhoto.ContentType,
                    request.UserName ?? user.UserName!
                    );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload profile photo for user {UserId}", user.Id);
                return new OperationResult
                {
                    Success = false,
                    Message = "Failed to upload profile photo"
                };
            }
        }

        string? newProfileBannerUrl = null;
        if (request.ProfileBanner != null)
        {
            try
            {
                var stream = request.ProfileBanner.OpenReadStream();
                newProfileBannerUrl = await fileUploadService.UploadImageAsync(
                    stream,
                    request.ProfileBanner.FileName,
                    request.ProfileBanner.ContentType,
                    (request.UserName ?? user.UserName!) + "-banner"
                    );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload profile banner for user {UserId}", user.Id);
                return new OperationResult
                {
                    Success = false,
                    Message = "Failed to upload profile banner"
                };
            }
        }
        mapper.Map(request, user);
        if (!string.IsNullOrWhiteSpace(newProfilePhotoUrl))
        {
            user.ProfilePhoto = newProfilePhotoUrl;
        }

        if (!string.IsNullOrWhiteSpace(newProfileBannerUrl))
        {
            user.ProfileBanner = newProfileBannerUrl;
        }

        var updatedResult = await userManager.UpdateAsync(user);
        if (!updatedResult.Succeeded)
        {
            var errors = string.Join(", ", updatedResult.Errors.Select(e => e.Description));
            logger.LogWarning("Failed to update user {UserId}: {errors}", user.Id, errors);

            return new OperationResult
            {
                Success = false,
                Message = $"Failed to update user: {errors}"
            };
        }

        // Queue for Elasticsearch update
        await searchIndexQueue.QueueUserUpdateAsync(user.Id);
        logger.LogDebug("Queued user {UserId} for search index update", user.Id);

        return new OperationResult
        {
            Success = true,
            Message = "Profile updated successfully"
        };
    }

}
