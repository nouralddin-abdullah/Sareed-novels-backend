using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using AutoMapper;
using Domain.Entities;

namespace Application.Chapters.DTOS;

public class ChapterProfiles : Profile
{
    public ChapterProfiles()
    {
        // The status is set with Chapter.SetStatus, which stamps when the chapter first comes out (#33). The schedule is
        // set by the handlers once checked (ChapterSchedule, #77).
        CreateMap<CreateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.PublishAt, opt => opt.Ignore());
        CreateMap<UpdateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.PublishAt, opt => opt.Ignore())
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
        // When a chapter came out is sent as UTC with "Z" (#39): SQL Server gives dates back without a kind. So is when a
        // draft publishes itself (#77).
        CreateMap<Chapter, ChaptersDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        CreateMap<Chapter, ChaptersAuthorDTO>()
            .IncludeBase<Chapter, ChaptersDTO>()
            .ForMember(dest => dest.PublishAt, opt => opt.MapFrom(src => AsUtc(src.PublishAt)));
        CreateMap<Chapter, ChapterSingleAuthorDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)))
            .ForMember(dest => dest.PublishAt, opt => opt.MapFrom(src => AsUtc(src.PublishAt)));
        CreateMap<Chapter, ChapterSingleReaderDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner))
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        CreateMap<ChapterParagraph, ChapterParagraphDTO>();
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null;
}
