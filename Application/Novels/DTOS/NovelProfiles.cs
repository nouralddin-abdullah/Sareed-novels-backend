using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.UpdateNovel;
using AutoMapper;
using Domain.Entities;

namespace Application.Novels.DTOS;

public class NovelProfiles : Profile
{
    public NovelProfiles()
    {
        CreateMap<CreateNovelCommand, Novel>();
        CreateMap<UpdateNovelCommand, Novel>()
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));

        //Quires
        CreateMap<Novel, MyWorksDTO>();
        CreateMap<Novel, WorkDTO>();


        //Reading

        //Commands

        //Quires
        CreateMap<User, AuthorDTO>();
        CreateMap<Genre, GenreSmallDto>();
        CreateMap<Novel, NovelsDTO>()
            .ForMember(dest => dest.Author, opt => opt.MapFrom(src => src.Owner))
            .ForMember(dest => dest.GenresList, opt => opt.MapFrom(src => src.NovelGenres.Select(ng => ng.Genre)));

        CreateMap<Novel, NovelInRankingDto>()
            .ForMember(dest => dest.GenresList, opt => opt.MapFrom(src => src.NovelGenres.Select(ng => ng.Genre)));
    }
}
