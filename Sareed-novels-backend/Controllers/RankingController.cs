using Application.Rankings.Queries.GetGenreRanking;
//using Application.Rankings.Queries.GetSiteWideRanking;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/rankings")]
public class RankingController(IMediator mediator) : ControllerBase
{
    [HttpGet("{genreSlug}/{rankingType}")]
    public async Task<IActionResult> GetGenreRanking(
        [FromRoute] string genreSlug,
        [FromRoute] string rankingType,
        [FromQuery] GetGenreRankingRequest request)
    {
        var query = new GetGenreRankingQuery(
            genreSlug,
            rankingType,
            request.PageSize ?? 10,
            request.PageNumber ?? 1);

        var result = await mediator.Send(query);
        return Ok(result);
    }

    //[HttpGet("site-wide/{rankingType}")]
    //public async Task<IActionResult> GetSiteWideRanking(
    //    [FromRoute] string rankingType,
    //    [FromQuery] GetSiteWideRankingRequest request)
    //{
    //    var query = new GetSiteWideRankingQuery(
    //        rankingType,
    //        request.PageSize ?? 20,
    //        request.PageNumber ?? 1);

    //    var result = await mediator.Send(query);
    //    return Ok(result);
    //}
}