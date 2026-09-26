using Application.Seo.Queries.GetSitemap;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/seo")]
public class SeoController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Every public novel with its published chapters, author, genres and indexable wiki entries, so the Cloudflare
    /// worker can build sitemap.xml in one request (one call per novel would exceed the worker's subrequest limit).
    /// </summary>
    [HttpGet("sitemap")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetSitemap(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new GetSitemapQuery(), cancellationToken));
}
