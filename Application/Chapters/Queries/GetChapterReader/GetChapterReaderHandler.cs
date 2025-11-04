using Application.Chapters.DTOS;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Chapters.Queries.GetChapterReader;

public class GetChapterReaderHandler(IChaptersRepository chaptersRepository, IChapterParagraphsRepository paragraphsRepository, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<GetChapterReaderQuery, ChapterSingleReaderDTO>
{
    public async Task<ChapterSingleReaderDTO> Handle(GetChapterReaderQuery request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("This chapter wasn't found");
        
        // Load paragraphs
        var paragraphs = await paragraphsRepository.GetChapterParagraphs(chapter.Id);
        
        var chapterDTO = mapper.Map<ChapterSingleReaderDTO>(chapter);
        chapterDTO.Paragraphs = mapper.Map<List<ChapterParagraphDTO>>(paragraphs);
        chapterDTO.NextChapterSlug = await chaptersRepository.GetNextChapterSlug(request.NovelId, chapter.ChapterIndex);
        
        return chapterDTO;
    }
}
