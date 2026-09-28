using Application.Users;
using Application.Users.Commands.FollowUser;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Novels.Commands.PublishWork;

public class PublishWorkCommandHandler(INovelsRepository novelsRepository, IUserContext userContext) : IRequestHandler<PublishWorkCommand, OperationResult>
{
    public async Task<OperationResult> Handle(PublishWorkCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        }
        novel.IsDraft = false;
        novel.IsEligibleForRanking = true;
        var result = await novelsRepository.UpdateOne(novel);
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "نُشرت الرواية"
            };
        }

        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "تعذّر نشر الرواية. حاول مرة أخرى."
        };
    }
}
