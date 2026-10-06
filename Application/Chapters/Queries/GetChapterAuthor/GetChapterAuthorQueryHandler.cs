using Application.Chapters.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Chapters.Queries.GetChapterAuthor;

public class GetChapterAuthorQueryHandler(IChaptersRepository chaptersRepository, IChapterParagraphsRepository paragraphsRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper, IPrivilegeService privilegeService) : IRequestHandler<GetChapterAuthorQuery, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(GetChapterAuthorQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        // Only chapters of the caller's own novel; otherwise any author could read any draft.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        // Load paragraphs from database
        var paragraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
        
        var chapterDTO = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(paragraphs);

        // Early access (#94) as non-subscribers meet it.
        var earlyAccess = await privilegeService.GetViewAsync(novel.Id, novel.AuthorId, viewerId: null);
        chapterDTO.IsLocked = earlyAccess.IsLocked(chapter);
        chapterDTO.UnlocksAt = earlyAccess.UnlocksAt(chapter);
        
        return chapterDTO;
    }
}
