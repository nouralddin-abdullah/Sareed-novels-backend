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
        // When a chapter came out is sent as UTC with "Z" (#39): SQL Server gives dates back without a kind.
        CreateMap<Chapter, ChaptersDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        CreateMap<Chapter, ChapterSingleAuthorDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        CreateMap<Chapter, ChapterSingleReaderDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner))
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        CreateMap<ChapterParagraph, ChapterParagraphDTO>();
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null;
}
