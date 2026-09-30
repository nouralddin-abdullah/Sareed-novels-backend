using Application.Novels.DTOS;
using AutoMapper;
using Domain.Entities;

namespace Application.Competitions.DTOs;

public class CompetitionProfile : Profile
{
    public CompetitionProfile()
    {
        // Competition -> CompetitionDto (list view). Status and CanJoin are as of the time the map was given (MapAt).
        CreateMap<Competition, CompetitionDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom((src, _, _, context) => src.EffectiveStatus(CompetitionMapping.UtcNow(context))))
            .ForMember(dest => dest.ParticipantCount, opt => opt.MapFrom(src => src.Participants.Count))
            .ForMember(dest => dest.CanJoin, opt => opt.MapFrom((src, _, _, context) => src.CanJoin(CompetitionMapping.UtcNow(context))));

        // Competition -> CompetitionDetailDto
        CreateMap<Competition, CompetitionDetailDto>()
            .ForMember(dest => dest.Status, opt => opt.MapFrom((src, _, _, context) => src.EffectiveStatus(CompetitionMapping.UtcNow(context))))
            .ForMember(dest => dest.ParticipantCount, opt => opt.MapFrom(src => src.Participants.Count))
            .ForMember(dest => dest.CanJoin, opt => opt.MapFrom((src, _, _, context) => src.CanJoin(CompetitionMapping.UtcNow(context))))
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
            .ForMember(dest => dest.CompetitionStatus, opt => opt.MapFrom((src, _, _, context) => src.Competition.EffectiveStatus(CompetitionMapping.UtcNow(context))))
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

/// <summary>
/// A competition's status depends on the time (<see cref="Competition.EffectiveStatus"/>), so every map to a DTO that
/// carries one (a competition, or a participation with its competition) is given that time: one for the whole answer,
/// the same the handler's query used.
/// </summary>
public static class CompetitionMapping
{
    /// <summary>Maps <paramref name="source"/> with every competition status and canJoin as at <paramref name="utcNow"/>.</summary>
    public static TDestination MapAt<TDestination>(this IMapper mapper, object source, DateTime utcNow) =>
        mapper.Map<TDestination>(source, options => options.State = new StatusTime(utcNow));

    /// <summary>The time the map was given; a map without one is a bug, so it fails instead of guessing.</summary>
    internal static DateTime UtcNow(ResolutionContext context) =>
        context.State is StatusTime time
            ? time.UtcNow
            : throw new InvalidOperationException(
                "A competition's status depends on the time: map it with CompetitionMapping.MapAt(source, utcNow).");

    private sealed record StatusTime(DateTime UtcNow);
}
