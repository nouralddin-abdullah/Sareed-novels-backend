using AutoMapper;
using Domain.Entities;

namespace Application.Genres.DTOS;

public class GenreProfile : Profile
{
    public GenreProfile()
    {
        CreateMap<Genre, GenresDTO>();
    }
}
