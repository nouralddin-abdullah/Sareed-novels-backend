using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using AutoMapper;
using Domain.Entities;

namespace Application.Chapters.DTOS;

public class ChapterProfiles : Profile
{
    public ChapterProfiles()
    {
        // The status is set with Chapter.SetStatus, which stamps when the chapter first comes out (#33).
        CreateMap<CreateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore());
        CreateMap<UpdateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
        CreateMap<Chapter, ChaptersDTO>();
        CreateMap<Chapter, ChapterSingleAuthorDTO>();
        CreateMap<Chapter, ChapterSingleReaderDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner));
        CreateMap<ChapterParagraph, ChapterParagraphDTO>();
    }
}
