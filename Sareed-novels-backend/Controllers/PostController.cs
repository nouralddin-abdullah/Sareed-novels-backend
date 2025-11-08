using Application.Posts.Commands.CreatePost;
using Application.Posts.Commands.DeletePost;
using Application.Posts.Commands.LikePost;
using Application.Posts.Commands.UnlikePost;
using Application.Posts.Queries.GetPost;
using Application.Posts.Queries.GetUserPosts;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/posts")]
[Authorize]
public class PostController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreatePost([FromForm] CreatePostRequest request)
    {
        var command = new CreatePostCommand(request.Content, request.Image, request.NovelId);
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{postId}")]
    public async Task<IActionResult> DeletePost([FromRoute] Guid postId)
    {
        var command = new DeletePostCommand(postId);
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return NoContent();
    }

    [HttpGet("{postId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPost([FromRoute] Guid postId)
    {
        var query = new GetPostQuery(postId);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("user/{userId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetUserPosts([FromRoute] string userId, [FromQuery] int? pageSize, [FromQuery] int? pageNumber)
    {
        var query = new GetUserPostsQuery(userId, pageNumber ?? 1, pageSize ?? 10);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("{postId}/like")]
    public async Task<IActionResult> LikePost([FromRoute] Guid postId)
    {
        var command = new LikePostCommand(postId);
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }

    [HttpDelete("{postId}/unlike")]
    public async Task<IActionResult> UnlikePost([FromRoute] Guid postId)
    {
        var command = new UnlikePostCommand(postId);
        var result = await mediator.Send(command);
        if (!result.Success)
        {
            return BadRequest(result);
        }
        return Ok(result);
    }
}
