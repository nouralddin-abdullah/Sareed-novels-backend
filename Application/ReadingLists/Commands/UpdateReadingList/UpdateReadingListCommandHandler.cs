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
    /// <summary>The code of an edit that sends a new picture and asks to remove the picture.</summary>
    public const string CoverConflictCode = "CoverConflict";
    public const string CoverConflictMessage = "لا يمكن رفع صورة جديدة وإزالة الصورة في الطلب نفسه";

    public async Task<OperationResult> Handle(UpdateReadingListCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Updating reading list {ListId} for user {UserId}", request.ReadingListId, currentUser.Id);

        if (request.RemoveCover && request.CoverImage != null)
        {
            return new OperationResult
            {
                Success = false,
                Code = CoverConflictCode,
                Message = CoverConflictMessage
            };
        }

        var readingList = await readingListsRepository.GetByIdAsync(request.ReadingListId)
            ?? throw new NotFoundException("القائمة غير موجودة", "ReadingListNotFound");

        if (readingList.UserId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لصاحب القائمة فقط", "NotOwner");
        }

        // Check name uniqueness if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != readingList.Name)
        {
            if (await readingListsRepository.IsNameTakenByUserAsync(currentUser.Id, request.Name, request.ReadingListId))
            {
                return new OperationResult
                {
                    Success = false,
                    Code = "DuplicateListName",
                    Message = $"لديك قائمة قراءة باسم «{request.Name}» بالفعل"
                };
            }
            readingList.Name = request.Name;
        }

        // Left out (null), it stays; sent empty or blank, it is removed (stored as null, as a list created without one).
        if (request.Description != null)
        {
            readingList.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description;
        }

        // Update visibility if provided
        if (request.IsPublic.HasValue)
        {
            readingList.IsPublic = request.IsPublic.Value;
        }

        if (request.RemoveCover)
        {
            // Only the list lets go of it: the stored file stays, as when a new picture replaces it.
            readingList.CoverImageUrl = null;
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
                    Code = "UploadFailed",
                    Message = "تعذّر رفع صورة القائمة. حاول مرة أخرى."
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
                Message = "حُفظت التغييرات"
            };
        }

        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "تعذّر حفظ التغييرات على القائمة. حاول مرة أخرى."
        };
    }
}
