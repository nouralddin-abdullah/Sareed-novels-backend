using Application.Novels.DTOS;
using AutoMapper;
using Domain.Entities;

namespace Application.Rankings.DTO;

public class RankingsProfile : Profile
{
    public RankingsProfile()
    {
        CreateMap<RankingEntry, NovelInRankingDto>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Novel.Id))
            .ForMember(dest => dest.Title, opt => opt.MapFrom(src => src.Novel.Title))
            .ForMember(dest => dest.Slug, opt => opt.MapFrom(src => src.Novel.Slug))
            .ForMember(dest => dest.CoverImageUrl, opt => opt.MapFrom(src => src.Novel.CoverImageUrl))
            .ForMember(dest => dest.Summary, opt => opt.MapFrom(src => src.Novel.Summary))
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.Novel.Status))
            .ForMember(dest => dest.TotalViews, opt => opt.MapFrom(src => src.Novel.TotalViews))
            .ForMember(dest => dest.TotalAverageScore, opt => opt.MapFrom(src => src.Novel.TotalAverageScore))
            .ForMember(dest => dest.ReviewCount, opt => opt.MapFrom(src => src.Novel.ReviewCount))
            .ForMember(dest => dest.GenresList, opt => opt.MapFrom(src => src.Novel.NovelGenres.Select(ng => ng.Genre)));
    }
}
