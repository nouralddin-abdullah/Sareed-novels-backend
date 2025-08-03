using Application.Novels.Queries.GetNovel;
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
    }
}
