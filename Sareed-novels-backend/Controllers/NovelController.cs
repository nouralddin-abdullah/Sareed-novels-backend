using Application.Novels.Queries.GetAllNovels;
using Application.Novels.Queries.GetNovel;
using Application.Novels.Queries.GetNovelRecommendations;
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

        [HttpGet("{novelId:guid}/recommendations")]
        public async Task<IActionResult> GetRecommendations(
            [FromRoute] Guid novelId,
            [FromQuery] int count = 10)
        {
            var query = new GetNovelRecommendationsQuery
            {
                NovelId = novelId,
                Count = Math.Min(count, 20) // Max 20 recommendations
            };
            var result = await mediator.Send(query);
            return Ok(result);
        }
    }
}

