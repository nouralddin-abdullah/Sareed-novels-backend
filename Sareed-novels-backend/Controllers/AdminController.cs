using Application.Covers.Commands.ConvertLegacyCovers;
using Application.Covers.Queries.GetCoverStatus;
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
    // Novel covers (see Application.Covers.NovelCovers)

    /// <summary>How many covers are in the standard format, and whether this host can process images.</summary>
    [HttpGet("covers/status")]
    public async Task<IActionResult> GetCoverStatus() => Ok(await mediator.Send(new GetCoverStatusQuery()));

    /// <summary>
    /// Converts the next batch of legacy covers. Call repeatedly, passing the returned nextCursor as after, until
    /// nextCursor is null; dryRun=true reads and processes without writing anything.
    /// </summary>
    [HttpPost("covers/convert")]
    public async Task<IActionResult> ConvertLegacyCovers([FromQuery] int batchSize = 10, [FromQuery] Guid? after = null, [FromQuery] bool dryRun = false)
    {
        var result = await mediator.Send(new ConvertLegacyCoversCommand { BatchSize = batchSize, After = after, DryRun = dryRun });
        return result.ProcessorAvailable ? Ok(result) : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }

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
}

public class RejectRechargeRequestDto
{
    public string RejectionReason { get; set; } = default!;
}

public class RejectWithdrawalRequestDto
{
    public string RejectionReason { get; set; } = default!;
}
