using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Application.Genres.DTOS;
using AutoMapper;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Genres.Queries;

public class GetAllGenresQueryHanlder(ILogger<GetAllGenresQueryHanlder> logger, IGenresRepository genresRepository, IMapper mapper) : IRequestHandler<GetAllGenresQuery, IEnumerable<GenresDTO>>
{
    public async Task<IEnumerable<GenresDTO>> Handle(GetAllGenresQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting all genres");
        var genres = await genresRepository.GetAllGenres();
        var result = mapper.Map<IEnumerable<GenresDTO>>(genres);
        return result;

    }
}
