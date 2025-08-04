using Application.Genres.Queries;
using Application.Novels.Queries.GetPopularByGenre;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/genre")]
    public class GenreController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> GetAllGenres()
        {
            var query = new GetAllGenresQuery();
            var result = await mediator.Send(query);
            return Ok(result);
        }

        [HttpGet("{genreSlug}/novels")]
        public async Task<IActionResult> GetNovelsInGenreRanking([FromRoute] string genreSlug, [FromQuery] GetNovelsInGenreRequest request)
        {
            var query = new GetNovelsInGenreQuery(genreSlug, request.PageSize ?? 10, request.PageNumber ?? 1, request.Sorting ?? "popular", request.IsCompleted ?? false);
            var novelDto = await mediator.Send(query);
            return Ok(novelDto);
        }
    }
}

