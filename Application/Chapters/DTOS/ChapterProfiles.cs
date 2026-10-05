using Application.Chapters.Commands.CreateChapter;
using Application.Chapters.Commands.UpdateChapter;
using AutoMapper;
using Domain.Entities;

namespace Application.Chapters.DTOS;

public class ChapterProfiles : Profile
{
    public ChapterProfiles()
    {
        // The status is set with Chapter.SetStatus, which stamps when the chapter first comes out (#33). The text is
        // stored as paragraphs in chapter format v1 (#74), never as sent: the legacy Chapters.Content column isn't
        // written from a request.
        CreateMap<CreateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.Content, opt => opt.Ignore());
        CreateMap<UpdateChapterCommand, Chapter>()
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.Content, opt => opt.Ignore())
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
        // When a chapter came out is sent as UTC with "Z" (#39): SQL Server gives dates back without a kind.
        CreateMap<Chapter, ChaptersDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        // When it was last saved too (#75), also sent with "Z".
        CreateMap<Chapter, ChaptersAuthorDTO>()
            .IncludeBase<Chapter, ChaptersDTO>()
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.UpdatedAt, DateTimeKind.Utc)));
        CreateMap<Chapter, ChapterSingleAuthorDTO>()
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)))
            .ForMember(dest => dest.UpdatedAt, opt => opt.MapFrom(src => DateTime.SpecifyKind(src.UpdatedAt, DateTimeKind.Utc)));
        CreateMap<Chapter, ChapterSingleReaderDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner))
            .ForMember(dest => dest.PublishedAt, opt => opt.MapFrom(src => AsUtc(src.PublishedAt)));
        // A paragraph always leaves the API in chapter format v1 (#74), however it is stored.
        CreateMap<ChapterParagraph, ChapterParagraphDTO>().ConvertUsing(paragraph => ChapterParagraphDTO.Of(paragraph));
    }

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } utc ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : null;
}
