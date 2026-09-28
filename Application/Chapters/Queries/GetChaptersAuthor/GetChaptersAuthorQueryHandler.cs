using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQueryHandler(ILogger<GetChaptersAuthorQueryHandler> logger, IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetChaptersAuthorQuery, IEnumerable<ChaptersDTO>>
{
    public async Task<IEnumerable<ChaptersDTO>> Handle(GetChaptersAuthorQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting author view chapters for {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        var chapters = await chaptersRepository.GetChaptersAuthorView(request.NovelId);
        var result = mapper.Map<IEnumerable<ChaptersDTO>>(chapters);
        return result;
    }
}
