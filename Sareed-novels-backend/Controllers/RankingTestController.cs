using Application.Rankings.Commands.CalculateAllRankings;
using Application.Rankings.Commands.CalculateGenreRankings;
using Application.Rankings.Queries.GetRankingStatus;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/admin/ranking-test")]
public class RankingTestController(IMediator mediator) : ControllerBase
{

    [HttpPost("calculate-all")]
    public async Task<IActionResult> CalculateAllRankings()
    {
        var command = new CalculateAllRankingsCommand();
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    [HttpPost("calculate-genre/{genreId}")]
    public async Task<IActionResult> CalculateGenreRankings(
        [FromRoute] int genreId,
        [FromQuery] string rankingType = "TopRated")
    {
        var command = new CalculateGenreRankingsCommand(genreId, rankingType);
        var result = await mediator.Send(command);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetRankingStatus()
    {
        var query = new GetRankingStatusQuery();
        var result = await mediator.Send(query);

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

}