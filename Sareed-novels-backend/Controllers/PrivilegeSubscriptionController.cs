using Application.Privileges.Queries.GetMySubscriptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
