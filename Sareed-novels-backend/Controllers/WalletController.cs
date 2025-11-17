using Application.Wallet.Commands.RequestRecharge;
using Application.Wallet.Commands.RequestWithdrawal;
using Application.Wallet.Queries.GetMyRechargeHistory;
using Application.Wallet.Queries.GetMyTransactionHistory;
using Application.Wallet.Queries.GetMyWallet;
using Application.Wallet.Queries.GetMyWithdrawalHistory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public class WalletController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetMyWallet()
    {
        var query = new GetMyWalletQuery();
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetMyTransactionHistory([FromQuery] int? pageNumber, [FromQuery] int? pageSize)
    {
        var query = new GetMyTransactionHistoryQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20
        };
        var (transactions, totalCount) = await mediator.Send(query);
        return Ok(new { transactions, totalCount });
    }

    [HttpPost("recharge")]
    public async Task<IActionResult> RequestRecharge([FromForm] RequestRechargeRequest request)
    {
        var command = new RequestRechargeCommand
        {
            PointsRequested = request.PointsRequested,
            PaymentMethod = request.PaymentMethod,
            PaymentProof = request.PaymentProof
        };
        
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    [HttpGet("recharge")]
    public async Task<IActionResult> GetMyRechargeHistory([FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? status)
    {
        var query = new GetMyRechargeHistoryQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 10,
            Status = status
        };
        var (requests, totalCount) = await mediator.Send(query);
        return Ok(new { requests, totalCount });
    }

    [HttpPost("withdraw")]
    public async Task<IActionResult> RequestWithdrawal([FromBody] RequestWithdrawalRequest request)
    {
        var command = new RequestWithdrawalCommand
        {
            PointsRequested = request.PointsRequested,
            WithdrawalMethod = request.WithdrawalMethod,
            PaymentDetails = request.PaymentDetails
        };
        
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result.Message);
        }
        return Ok(result);
    }

    [HttpGet("withdraw")]
    public async Task<IActionResult> GetMyWithdrawalHistory([FromQuery] int? pageNumber, [FromQuery] int? pageSize, [FromQuery] string? status)
    {
        var query = new GetMyWithdrawalHistoryQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 10,
            Status = status
        };
        var (requests, totalCount) = await mediator.Send(query);
        return Ok(new { requests, totalCount });
    }
}

public class RequestRechargeRequest
{
    public int PointsRequested { get; set; }
    public string PaymentMethod { get; set; } = default!;
    public IFormFile PaymentProof { get; set; } = default!;
}

public class RequestWithdrawalRequest
{
    public int PointsRequested { get; set; }
    public string WithdrawalMethod { get; set; } = default!;
    public string PaymentDetails { get; set; } = default!;
}
