using Application.Reports.Commands.CreateReport;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sareed_novels_backend.Extensions;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Reports a comment, review, post, user, novel or reading list to the moderators: 201 with the new report, or 200
    /// with the reporter's open report on the same target (nothing new is saved). The admin side is under /api/admin/reports.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Reports)]
    public async Task<IActionResult> CreateReport([FromBody] CreateReportCommand command)
    {
        var result = await mediator.Send(command);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result.Report) : Ok(result.Report);
    }
}
