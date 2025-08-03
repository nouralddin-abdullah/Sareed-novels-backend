using Application.Genres.DTOS;
using MediatR;

namespace Application.Genres.Queries;

public class GetAllGenresQuery : IRequest<IEnumerable<GenresDTO>>
{
}
