using Application.Genres.Queries;
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
    }
}

