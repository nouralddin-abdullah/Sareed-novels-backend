using Application.Novels.Queries.GetAllNovels;
using Application.Novels.Queries.GetNovel;
using Application.Novels.Queries.GetPopularByGenre;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers
{
    [ApiController]
    [Route("api/novel")]
    public class NovelController(IMediator mediator) : ControllerBase
    {
        [HttpGet("{novelSlug}")]
        public async Task<IActionResult> GetNovelBySlug([FromRoute] string novelSlug)
        {
            var query = new GetNovelQuery(novelSlug);
            var novelDto = await mediator.Send(query);
            return Ok(novelDto);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllNovels(
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 100)
        {
            var query = new GetAllNovelsQuery
            {
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            var result = await mediator.Send(query);
            return Ok(result);
        }
    }
}
