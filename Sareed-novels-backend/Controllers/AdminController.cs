using Application.Services;
using Application.Wallet.Commands.ApproveRecharge;
using Application.Wallet.Commands.ApproveWithdrawal;
using Application.Wallet.Commands.RejectRecharge;
using Application.Wallet.Commands.RejectWithdrawal;
using Application.Wallet.Queries.GetPendingRechargeRequests;
using Application.Wallet.Queries.GetPendingWithdrawalRequests;
using Domain.Constants;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = UserRoles.Admin)] // ✅ Admin role required
public class AdminController(IMediator mediator) : ControllerBase
{
    // Wallet Management Endpoints
    [HttpGet("recharge/pending")]
    public async Task<IActionResult> GetPendingRechargeRequests([FromQuery] int? pageNumber, [FromQuery] int? pageSize)
    {
        var query = new GetPendingRechargeRequestsQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20
        };
        var (requests, totalCount) = await mediator.Send(query);
        return Ok(new { requests, totalCount });
    }

    [HttpPatch("recharge/{id}/approve")]
    public async Task<IActionResult> ApproveRecharge([FromRoute] Guid id)
    {
        var command = new ApproveRechargeCommand { RequestId = id };
        var result = await mediator.Send(command);
        
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    [HttpPatch("recharge/{id}/reject")]
    public async Task<IActionResult> RejectRecharge([FromRoute] Guid id, [FromBody] RejectRechargeRequestDto request)
    {
        var command = new RejectRechargeCommand
        {
            RequestId = id,
            RejectionReason = request.RejectionReason
        };
        var result = await mediator.Send(command);
        
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    [HttpGet("withdraw/pending")]
    public async Task<IActionResult> GetPendingWithdrawalRequests([FromQuery] int? pageNumber, [FromQuery] int? pageSize)
    {
        var query = new GetPendingWithdrawalRequestsQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20
        };
        var (requests, totalCount) = await mediator.Send(query);
        return Ok(new { requests, totalCount });
    }

    [HttpPatch("withdraw/{id}/approve")]
    public async Task<IActionResult> ApproveWithdrawal([FromRoute] Guid id)
    {
        var command = new ApproveWithdrawalCommand { RequestId = id };
        var result = await mediator.Send(command);
        
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    [HttpPatch("withdraw/{id}/reject")]
    public async Task<IActionResult> RejectWithdrawal([FromRoute] Guid id, [FromBody] RejectWithdrawalRequestDto request)
    {
        var command = new RejectWithdrawalCommand
        {
            RequestId = id,
            RejectionReason = request.RejectionReason
        };
        var result = await mediator.Send(command);
        
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    // Elasticsearch Management Endpoints
    [HttpPost("elasticsearch/init")]
    public async Task<IActionResult> InitializeElasticsearchIndexes(
        [FromServices] IEntitySearchService entitySearchService,
        [FromServices] INovelSearchService novelSearchService,
        [FromServices] IUserSearchService userSearchService)
    {
        var results = new
        {
            EntityIndex = await entitySearchService.EnsureIndexExistsAsync(),
            NovelIndex = await novelSearchService.EnsureIndexExistsAsync(),
            UserIndex = await userSearchService.EnsureIndexExistsAsync()
        };

        return Ok(new
        {
            success = results.EntityIndex && results.NovelIndex && results.UserIndex,
            message = "Elasticsearch indexes initialization attempted",
            details = results
        });
    }

    [HttpPost("elasticsearch/reindex-entities/{novelId}")]
    public async Task<IActionResult> ReindexNovelEntities(
        [FromRoute] Guid novelId,
        [FromServices] IEntitySearchService entitySearchService)
    {
        var count = await entitySearchService.ReindexNovelEntitiesAsync(novelId);
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} entities for novel {novelId}",
            count
        });
    }

    [HttpPost("elasticsearch/reindex-novels")]
    public async Task<IActionResult> ReindexNovels(
        [FromServices] INovelSearchService novelSearchService)
    {
        var count = await novelSearchService.ReindexAllNovelsAsync();
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} novels",
            count
        });
    }

    [HttpPost("elasticsearch/reindex-users")]
    public async Task<IActionResult> ReindexUsers(
        [FromServices] IUserSearchService userSearchService)
    {
        var count = await userSearchService.ReindexAllUsersAsync();
        
        return Ok(new
        {
            success = count > 0,
            message = $"Reindexed {count} users",
            count
        });
    }
}

public class RejectRechargeRequestDto
{
    public string RejectionReason { get; set; } = default!;
}

public class RejectWithdrawalRequestDto
{
    public string RejectionReason { get; set; } = default!;
}
