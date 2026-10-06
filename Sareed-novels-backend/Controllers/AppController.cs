using Application.AppConfig.Queries.GetAppConfig;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/app")]
public class AppController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// What the mobile apps check at startup: the minimum and latest Android and iOS versions and the maintenance flag,
    /// from the "AppConfig" configuration section (see README). Cacheable for five minutes, so a change reaches apps
    /// within that. HEAD answers the same way without the body (#89): uptime monitors check with HEAD, and the one that
    /// keeps the API awake for scheduled chapters (#77) would otherwise see 405.
    /// </summary>
    [HttpGet("config")]
    [HttpHead("config")]
    [AllowAnonymous]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var config = await mediator.Send(new GetAppConfigQuery(), cancellationToken);

        // Set only once there is an answer ([ResponseCache] sets it before the action runs, so a misconfiguration's 500
        // would be cached as well).
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Public = true, MaxAge = TimeSpan.FromMinutes(5) };
        if (HttpMethods.IsHead(Request.Method))
        {
            // GET's headers without its body, whatever server runs the API.
            Response.ContentType = "application/json; charset=utf-8";
            return new EmptyResult();
        }

        return Ok(config);
    }
}
