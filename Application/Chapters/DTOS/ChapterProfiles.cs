using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using AutoMapper;
using Domain.Entities;

namespace Application.Chapters.DTOS;

public class ChapterProfiles : Profile
{
    public ChapterProfiles()
    {
        CreateMap<CreateChapterCommand, Chapter>();
        CreateMap<UpdateChapterCommand, Chapter>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
        CreateMap<Chapter, ChaptersDTO>();
        CreateMap<Chapter, ChapterSingleAuthorDTO>();
        CreateMap<Chapter, ChapterSingleReaderDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner));
    }
}
