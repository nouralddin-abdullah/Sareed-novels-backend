using Application.Common;
using Application.Gifts.Commands.CreateGift;
using Application.Gifts.Commands.DeleteGift;
using Application.Gifts.Commands.RecalculateLeaderboards;
using Application.Gifts.Commands.SendGift;
using Application.Gifts.Commands.UpdateGift;
using Application.Gifts.DTOs;
using Application.Gifts.Queries.GetAllGifts;
using Application.Gifts.Queries.GetGlobalLeaderboard;
using Application.Gifts.Queries.GetMyGiftHistory;
using Application.Gifts.Queries.GetNovelGifts;
using Application.Gifts.Queries.GetTopSupporters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/gift")]
public class GiftController(IMediator mediator) : ControllerBase
{
    // === PUBLIC ENDPOINTS ===

    [HttpGet]
    public async Task<ActionResult<PagedResult<GiftDto>>> GetAllGifts(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetAllGiftsQuery(pageNumber, pageSize);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("send")]
    [Authorize]
    public async Task<ActionResult<OperationResult>> SendGift([FromBody] SendGiftCommand command)
    {
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpGet("novel/{novelId}")]
    public async Task<ActionResult<PagedResult<GiftTransactionDto>>> GetNovelGifts(
        Guid novelId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetNovelGiftsQuery(novelId, pageNumber, pageSize);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("novel/{novelId}/top-supporters")]
    public async Task<ActionResult<List<TopSupporterDto>>> GetTopSupporters(
        Guid novelId,
        [FromQuery] int topCount = 10)
    {
        var query = new GetTopSupportersQuery(novelId, topCount);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("leaderboard/{period}")]
    public async Task<ActionResult<GlobalLeaderboardDto>> GetGlobalLeaderboard(
        string period,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        if (period != "Weekly" && period != "AllTime")
        {
            return BadRequest(new { message = "Period must be 'Weekly' or 'AllTime'" });
        }

        var query = new GetGlobalLeaderboardQuery(period, pageNumber, pageSize);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("my-history")]
    [Authorize]
    public async Task<ActionResult<PagedResult<GiftTransactionDto>>> GetMyGiftHistory(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetMyGiftHistoryQuery(pageNumber, pageSize);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    // === ADMIN ENDPOINTS ===

    [HttpPost("admin/create")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<GiftDto>> CreateGift([FromForm] CreateGiftCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPut("admin/update")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<bool>> UpdateGift([FromForm] UpdateGiftCommand command)
    {
        var result = await mediator.Send(command);
        if (!result)
        {
            return NotFound(new { message = "Gift not found" });
        }
        return Ok(new { success = true, message = "Gift updated successfully" });
    }

    [HttpDelete("admin/{giftId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> DeleteGift(Guid giftId)
    {
        var command = new DeleteGiftCommand(giftId);
        var result = await mediator.Send(command);
        if (!result)
        {
            return NotFound(new { message = "Gift not found" });
        }
        return Ok(new { success = true, message = "Gift deleted successfully" });
    }

    [HttpPost("admin/recalculate-weekly")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> RecalculateWeeklyLeaderboard()
    {
        var command = new RecalculateWeeklyLeaderboardCommand();
        await mediator.Send(command);
        return Ok(new { success = true, message = "Weekly leaderboard recalculated successfully" });
    }

    [HttpPost("admin/recalculate-alltime")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<bool>> RecalculateAllTimeLeaderboard()
    {
        var command = new RecalculateAllTimeLeaderboardCommand();
        await mediator.Send(command);
        return Ok(new { success = true, message = "All-time leaderboard recalculated successfully" });
    }
}
