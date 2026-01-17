using Application.Novels.DTOS;
using AutoMapper;
using Domain.Entities;

namespace Application.Competitions.DTOs;

public class CompetitionProfile : Profile
{
    public CompetitionProfile()
    {
        // Competition -> CompetitionDto (list view)
        CreateMap<Competition, CompetitionDto>()
            .ForMember(dest => dest.ParticipantCount, opt => opt.MapFrom(src => src.Participants.Count))
            .ForMember(dest => dest.CanJoin, opt => opt.MapFrom(src => src.CanJoin()));

        // Competition -> CompetitionDetailDto
        CreateMap<Competition, CompetitionDetailDto>()
            .ForMember(dest => dest.ParticipantCount, opt => opt.MapFrom(src => src.Participants.Count))
            .ForMember(dest => dest.CanJoin, opt => opt.MapFrom(src => src.CanJoin()))
            .ForMember(dest => dest.Winners, opt => opt.MapFrom(src => src.Winners));

        // CompetitionParticipant -> CompetitionParticipantDto
        CreateMap<CompetitionParticipant, CompetitionParticipantDto>()
            .ForMember(dest => dest.NovelTitle, opt => opt.MapFrom(src => src.Novel.Title))
            .ForMember(dest => dest.NovelSlug, opt => opt.MapFrom(src => src.Novel.Slug))
            .ForMember(dest => dest.NovelCoverImageUrl, opt => opt.MapFrom(src => src.Novel.CoverImageUrl))
            .ForMember(dest => dest.GenresList, opt => opt.MapFrom(src => src.Novel.NovelGenres.Select(ng => ng.Genre)))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner))
            .ForMember(dest => dest.CompetitionViews, opt => opt.MapFrom(src => src.Novel.TotalViews - src.ViewsAtJoin))
            .ForMember(dest => dest.TotalPoints, opt => opt.MapFrom(src => src.CurrentPoints + src.ExtraPoints));

        // CompetitionWinner -> CompetitionWinnerDto
        CreateMap<CompetitionWinner, CompetitionWinnerDto>()
            .ForMember(dest => dest.NovelTitle, opt => opt.MapFrom(src => src.Novel.Title))
            .ForMember(dest => dest.NovelSlug, opt => opt.MapFrom(src => src.Novel.Slug))
            .ForMember(dest => dest.NovelCoverImageUrl, opt => opt.MapFrom(src => src.Novel.CoverImageUrl))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Author));

        // CompetitionParticipant -> CompetitionLeaderboardEntryDto
        CreateMap<CompetitionParticipant, CompetitionLeaderboardEntryDto>()
            .ForMember(dest => dest.Rank, opt => opt.MapFrom(src => src.CurrentRank))
            .ForMember(dest => dest.NovelTitle, opt => opt.MapFrom(src => src.Novel.Title))
            .ForMember(dest => dest.NovelSlug, opt => opt.MapFrom(src => src.Novel.Slug))
            .ForMember(dest => dest.NovelCoverImageUrl, opt => opt.MapFrom(src => src.Novel.CoverImageUrl))
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Novel.Owner))
            .ForMember(dest => dest.TotalPoints, opt => opt.MapFrom(src => src.CurrentPoints + src.ExtraPoints))
            .ForMember(dest => dest.CompetitionViews, opt => opt.MapFrom(src => src.Novel.TotalViews - src.ViewsAtJoin));

        // CompetitionParticipant -> MyCompetitionParticipationDto
        CreateMap<CompetitionParticipant, MyCompetitionParticipationDto>()
            .ForMember(dest => dest.CompetitionName, opt => opt.MapFrom(src => src.Competition.Name))
            .ForMember(dest => dest.CompetitionSlug, opt => opt.MapFrom(src => src.Competition.Slug))
            .ForMember(dest => dest.CompetitionStatus, opt => opt.MapFrom(src => src.Competition.Status))
            .ForMember(dest => dest.NovelTitle, opt => opt.MapFrom(src => src.Novel.Title))
            .ForMember(dest => dest.NovelSlug, opt => opt.MapFrom(src => src.Novel.Slug))
            .ForMember(dest => dest.NovelCoverImageUrl, opt => opt.MapFrom(src => src.Novel.CoverImageUrl))
            .ForMember(dest => dest.TotalPoints, opt => opt.MapFrom(src => src.CurrentPoints + src.ExtraPoints))
            .ForMember(dest => dest.CompetitionViews, opt => opt.MapFrom(src => src.Novel.TotalViews - src.ViewsAtJoin));

        // Genre -> GenreSmallDto (if not already mapped elsewhere)
        CreateMap<Genre, GenreSmallDto>();
        
        // User -> AuthorDTO (if not already mapped elsewhere)
        CreateMap<User, AuthorDTO>();
    }
}
