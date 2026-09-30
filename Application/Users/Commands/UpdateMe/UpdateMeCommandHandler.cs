using Application.Services;
using Application.Common;
using Application.Users.Queries.GetMyProfile;
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
    ISender sender) : IRequestHandler<UpdateMeCommand, UpdateMeResult>
{
    public async Task<UpdateMeResult> Handle(UpdateMeCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Updating data for user {username}", currentUser.UserName);
        var user = await userManager.FindByIdAsync(currentUser.Id) ?? throw new NotFoundException("المستخدم غير موجود", "UserNotFound");

        // "deleted-..." names are deleted accounts' (the Identity validator refuses them too; this says so with its code).
        if (UserNameRules.LooksDeleted(request.UserName) && !string.Equals(request.UserName, user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateMeResult
            {
                Success = false,
                Code = UserNameRules.DeletedPrefixCode,
                Message = UserNameRules.DeletedPrefixMessage
            };
        }

        // A new user name must be free. Names are unique whatever their case, so a change of case only finds the member
        // themselves, which is fine (#25: it used to be refused as taken).
        if (!string.IsNullOrEmpty(request.UserName) && request.UserName != user.UserName)
        {
            var existingUser = await userManager.FindByNameAsync(request.UserName);
            if (existingUser != null && existingUser.Id != user.Id)
            {
                return new UpdateMeResult
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
                return new UpdateMeResult
                {
                    Success = false,
                    Code = "UploadFailed",
                    Message = "تعذّر رفع الصورة الشخصية. حاول مرة أخرى.",
                    Field = nameof(UpdateMeCommand.ProfilePhoto)
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
                return new UpdateMeResult
                {
                    Success = false,
                    Code = "UploadFailed",
                    Message = "تعذّر رفع صورة الغلاف. حاول مرة أخرى.",
                    Field = nameof(UpdateMeCommand.ProfileBanner)
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

            return new UpdateMeResult
            {
                Success = false,
                // Identity's code for the first problem (InvalidUserName, DuplicateUserName...), as register answers it.
                Code = updatedResult.Errors.FirstOrDefault()?.Code ?? "OperationFailed",
                Message = ArabicText.Sentences(["تعذّر تحديث الملف الشخصي", .. updatedResult.Errors.Select(e => e.Description)])
            };
        }

        return new UpdateMeResult
        {
            Success = true,
            Message = "تم تحديث الملف الشخصي",
            // Through GET /api/User/my-profile itself, after the save, so the app gets the profile exactly as my-profile
            // returns it and needn't read it again (#44).
            Profile = await sender.Send(new GetMyProfileQuery(), cancellationToken)
        };
    }

    private static string? ClearIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

}
