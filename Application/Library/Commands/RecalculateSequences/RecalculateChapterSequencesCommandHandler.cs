using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Commands.RecalculateSequences;

public class RecalculateChapterSequencesCommandHandler(
    ILogger<RecalculateChapterSequencesCommandHandler> logger,
    IChapterSequenceService sequenceService,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<RecalculateChapterSequencesCommand, OperationResult>
{
    public async Task<OperationResult> Handle(RecalculateChapterSequencesCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        
        var novel = await novelsRepository.GetOne(request.NovelId) 
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        }
        
        logger.LogInformation(
            "User {UserId} manually triggering sequence recalculation for novel {NovelId}", 
            currentUser.Id, request.NovelId);
        
        await sequenceService.RecalculateSequencesForNovelAsync(request.NovelId);
        await sequenceService.UpdateReadingProgressForNovelAsync(request.NovelId);
        
        return new OperationResult
        {
            Success = true,
            Message = "أُعيد حساب أرقام الفصول المنشورة"
        };
    }
}
