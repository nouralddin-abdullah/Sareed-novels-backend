using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQueryHandler(ILogger<GetChaptersAuthorQueryHandler> logger, IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper, IPrivilegeService privilegeService) : IRequestHandler<GetChaptersAuthorQuery, IEnumerable<ChaptersAuthorDTO>>
{
    public async Task<IEnumerable<ChaptersAuthorDTO>> Handle(GetChaptersAuthorQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting author view chapters for {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        var chapters = (await chaptersRepository.GetChaptersAuthorView(request.NovelId)).ToList();
        var result = mapper.Map<List<ChaptersAuthorDTO>>(chapters);

        // Early access (#94) as non-subscribers meet it: which chapters it locks, and until when.
        var earlyAccess = await privilegeService.GetViewAsync(novel.Id, novel.AuthorId, viewerId: null);
        foreach (var (dto, chapter) in result.Zip(chapters))
        {
            dto.IsLocked = dto.IsEarlyAccess = earlyAccess.IsLocked(chapter);
            dto.UnlocksAt = earlyAccess.UnlocksAt(chapter);
            dto.LockedAt = earlyAccess.LockedAt(chapter);
        }
        return result;
    }
}
