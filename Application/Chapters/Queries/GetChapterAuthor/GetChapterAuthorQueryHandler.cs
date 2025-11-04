using Application.Chapters.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Chapters.Queries.GetChapterAuthor;

public class GetChapterAuthorQueryHandler(IChaptersRepository chaptersRepository, IChapterParagraphsRepository paragraphsRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetChapterAuthorQuery, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(GetChapterAuthorQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("Chapter not found");
        
        // Load paragraphs from database
        var paragraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
        
        var chapterDTO = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(paragraphs);
        
        return chapterDTO;
    }
}
