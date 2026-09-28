using Application.Privileges.Queries.GetMySubscriptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Application.Common;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/privilege")]
[Authorize]
public class PrivilegeSubscriptionController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Get current user's privilege subscriptions
    /// </summary>
    [HttpGet("my-subscriptions")]
    public async Task<IActionResult> GetMySubscriptions(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10)
    {
        // The page it answers (and totalPages) are the clamped ones: pageSize=0 used to divide by zero.
        (pageNumber, pageSize) = Paging.Clamp(pageNumber, pageSize);
        var query = new GetMySubscriptionsQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        
        var (subscriptions, totalCount) = await mediator.Send(query);
        
        return Ok(new
        {
            subscriptions,
            totalCount,
            pageNumber,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }
}
