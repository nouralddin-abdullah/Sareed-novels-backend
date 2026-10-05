using Application.Chapters.Commands.UploadChapterImage;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Sareed_novels_backend.Extensions;

namespace Sareed_novels_backend.Controllers;

/// <summary>
/// Pictures for chapters (#86, README "Chapter pictures"): the chapter editor uploads one here, then saves its address
/// in an <c>&lt;img src&gt;</c> of the chapter (chapter format v1's image paragraph, #74).
/// </summary>
[ApiController]
[Route("api/novel/{novelId:guid}/chapter-images")]
[Authorize]
public class ChapterImageController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Stores a picture for the novel's chapters, for its author only (drafts too): multipart, one file in <c>image</c>
    /// (JPEG, PNG or WebP, at most 5 MB). 200 <c>{ url }</c>, the absolute address of the stored WebP (the whole picture
    /// turned upright, at most 2000 px on its long side, no metadata). Refusals are <c>{ code, message }</c>: 400
    /// ValidationFailed (no file, another declared type, over 5 MB), 400 cover_unsupported_format, cover_unreadable or
    /// cover_too_many_pixels (the bytes), 400 UploadFailed (it couldn't be processed or stored), 401, 403 NotOwner, 404
    /// NovelNotFound, 429 TooManyRequests (30 uploads in 10 minutes per address).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    public async Task<IActionResult> Upload([FromRoute] Guid novelId, [FromForm] UploadChapterImageRequest request,
        CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new UploadChapterImageCommand(novelId, request.Image!), cancellationToken));
}
