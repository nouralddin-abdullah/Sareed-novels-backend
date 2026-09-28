using Application.Services;
using Application.Common;
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
    IMapper mapper) : IRequestHandler<UpdateMeCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateMeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Updating data for user {username}", currentUser.UserName);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");

        //if username is provided and not null test if it was taken before? or available
        if (!string.IsNullOrEmpty(request.UserName) && request.UserName != user.UserName)
        {
            var existingUser = await userManager.FindByNameAsync(request.UserName);
            if (existingUser != null)
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "UserNameTaken",
                    Message = "اسم المستخدم مستخدم بالفعل، اختر اسمًا آخر"
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
                    user.Id
                    );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload profile photo for user {UserId}", user.Id);
                return new OperationResult
                {
                    Success = false,
                    Code = "UploadFailed",
                    Message = "تعذّر رفع الصورة الشخصية. حاول مرة أخرى."
                };
            }
        }

        string? newProfileBannerUrl = null;
        if (request.ProfileBanner != null)
        {
            try
            {
                var stream = request.ProfileBanner.OpenReadStream();
                newProfileBannerUrl = await fileUploadService.UploadProfileBannerAsync(
                    stream,
                    request.ProfileBanner.ContentType,
                    user.Id
                    );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to upload profile banner for user {UserId}", user.Id);
                return new OperationResult
                {
                    Success = false,
                    Code = "UploadFailed",
                    Message = "تعذّر رفع صورة الغلاف. حاول مرة أخرى."
                };
            }
        }
        mapper.Map(request, user);
        // The mapping copies what was sent; an empty (or blank) bio or link means "remove it".
        user.UserBio = ClearIfBlank(user.UserBio);
        user.FacebookUrl = ClearIfBlank(user.FacebookUrl);
        user.TwitterUrl = ClearIfBlank(user.TwitterUrl);
        user.DiscordUrl = ClearIfBlank(user.DiscordUrl);
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
                Code = "OperationFailed",
                Message = ArabicText.Sentences(["تعذّر تحديث الملف الشخصي", .. updatedResult.Errors.Select(e => e.Description)])
            };
        }

        return new OperationResult
        {
            Success = true,
            Message = "تم تحديث الملف الشخصي"
        };
    }

    private static string? ClearIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

}
