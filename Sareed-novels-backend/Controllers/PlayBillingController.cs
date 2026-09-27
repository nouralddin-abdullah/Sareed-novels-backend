using Application.Wallet.Commands.VerifyPlayPurchase;
using Application.Wallet.DTOs;
using Application.Wallet.Queries.GetPlayProducts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

/// <summary>
/// Point packs bought in the Android app through Google Play Billing (README.md, "Google Play point packs").
/// Not an [ApiController]: a missing or malformed body gets the same { code, message } error as every other failure,
/// not a ProblemDetails, so the app only ever reads <c>code</c>.
/// </summary>
[Authorize]
[Route("api/wallet")]
public class PlayBillingController(IMediator mediator) : ControllerBase
{
    /// <summary>The packs the app may sell: product ids and points, never prices (the app shows Play's).</summary>
    [HttpGet("play-products")]
    public async Task<ActionResult<PlayProductsDto>> GetPlayProducts() =>
        Ok(await mediator.Send(new GetPlayProductsQuery()));

    /// <summary>Verifies a Google Play purchase and credits its points, once per purchase token.</summary>
    [HttpPost("play-purchase")]
    public async Task<IActionResult> VerifyPlayPurchase([FromBody] PlayPurchaseRequest? request)
    {
        var result = await mediator.Send(new VerifyPlayPurchaseCommand
        {
            ProductId = request?.ProductId,
            PurchaseToken = request?.PurchaseToken,
            OrderId = request?.OrderId
        });

        if (result.Success)
        {
            return Ok(new PlayPurchaseResponse(result.PointsAdded, result.CurrentBalance));
        }

        return StatusCode(StatusCodeFor(result.Error!.Value), new PlayBillingError(result.Code!, result.Message!));
    }

    public static int StatusCodeFor(PlayPurchaseError error) => error switch
    {
        PlayPurchaseError.BillingUnavailable or PlayPurchaseError.VerificationUnavailable => StatusCodes.Status503ServiceUnavailable,
        PlayPurchaseError.AccountMismatch or PlayPurchaseError.TestPurchaseNotAllowed => StatusCodes.Status403Forbidden,
        PlayPurchaseError.AlreadyUsedByAnotherUser or PlayPurchaseError.PurchasePending
            or PlayPurchaseError.PurchaseCanceled or PlayPurchaseError.PurchaseVoided => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}

public class PlayPurchaseRequest
{
    public string? ProductId { get; set; }
    public string? PurchaseToken { get; set; }
    public string? OrderId { get; set; }
}

public record PlayPurchaseResponse(int PointsAdded, decimal CurrentBalance);

/// <summary><c>code</c> is stable (a <see cref="PlayPurchaseError"/> name) for the app to act on; <c>message</c> is Arabic, for the user.</summary>
public record PlayBillingError(string Code, string Message);
