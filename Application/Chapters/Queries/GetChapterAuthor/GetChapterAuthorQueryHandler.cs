using Application.Chapters.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Chapters.Queries.GetChapterAuthor;

public class GetChapterAuthorQueryHandler(IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetChapterAuthorQuery, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(GetChapterAuthorQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId);
        var chapterDTO = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        return chapterDTO;
    }
}
