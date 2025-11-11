using Application.Entities.Commands.AddArticle;
using Application.Entities.Commands.AddGalleryImage;
using Application.Entities.Commands.CreateEntity;
using Application.Entities.Commands.CreateRelationship;
using Application.Entities.Commands.DeleteArticle;
using Application.Entities.Commands.DeleteEntity;
using Application.Entities.Commands.DeleteRelationship;
using Application.Entities.Commands.RemoveGalleryImage;
using Application.Entities.Commands.ReorderArticles;
using Application.Entities.Commands.ReorderGalleryImages;
using Application.Entities.Commands.UpdateArticle;
using Application.Entities.Commands.UpdateEntity;
using Application.Entities.Commands.UpdateGalleryImage;
using Application.Entities.Commands.UpdateRelationship;
using Application.Entities.Queries.GetEntityById;
using Application.Entities.Queries.GetSections;
using Application.Entities.Queries.GetNovelEntities;
using Application.Entities.Queries.SearchEntities;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Sareed_novels_backend.Controllers;

[ApiController]
[Route("api/novels/{novelId}/entities")]
[Authorize]
public class EntityController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateEntity([FromRoute] Guid novelId, [FromForm] CreateEntityCommand command)
    {
        command.NovelId = novelId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPatch("{entityId}")]
    public async Task<IActionResult> UpdateEntity([FromRoute] Guid entityId, [FromForm] UpdateEntityCommand command)
    {
        command.EntityId = entityId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{entityId}")]
    public async Task<IActionResult> DeleteEntity([FromRoute] Guid entityId)
    {
        var command = new DeleteEntityCommand(entityId);
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return NoContent();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetNovelEntities(
        [FromRoute] Guid novelId,
        [FromQuery] string? section,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = new GetNovelEntitiesQuery
        {
            NovelId = novelId,
            Section = section,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("{entityId}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetEntity([FromRoute] Guid entityId)
    {
        var query = new GetEntityByIdQuery(entityId);
        var result = await mediator.Send(query);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<IActionResult> SearchEntities(
        [FromRoute] Guid novelId,
        [FromQuery] string? query,
        [FromQuery] string? section,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var searchQuery = new SearchEntitiesQuery
        {
            NovelId = novelId,
            Query = query,
            Section = section,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await mediator.Send(searchQuery);
        return Ok(result);
    }

    [HttpGet("sections")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSections([FromRoute] Guid novelId)
    {
        var query = new GetSectionsQuery(novelId);
        var result = await mediator.Send(query);
        return Ok(result);
    }

    [HttpPost("{entityId}/articles")]
    public async Task<IActionResult> AddArticle([FromRoute] Guid entityId, [FromBody] AddArticleCommand command)
    {
        command.EntityId = entityId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPatch("articles/{articleId}")]
    public async Task<IActionResult> UpdateArticle([FromRoute] Guid articleId, [FromBody] UpdateArticleCommand command)
    {
        command.ArticleId = articleId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("articles/{articleId}")]
    public async Task<IActionResult> DeleteArticle([FromRoute] Guid articleId)
    {
        var command = new DeleteArticleCommand(articleId);
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return NoContent();
    }

    [HttpPatch("{entityId}/articles/reorder")]
    public async Task<IActionResult> ReorderArticles(
        [FromRoute] Guid entityId,
        [FromBody] ReorderArticlesRequest request)
    {
        var command = new ReorderArticlesCommand
        {
            EntityId = entityId,
            OrderedArticleIds = request.OrderedArticleIds
        };
        var result = await mediator.Send(command);
        if (!result) return BadRequest("Failed to reorder articles");
        return Ok();
    }

    [HttpPost("relationships")]
    public async Task<IActionResult> CreateRelationship([FromBody] CreateRelationshipCommand command)
    {
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPatch("relationships/{relationshipId}")]
    public async Task<IActionResult> UpdateRelationship([FromRoute] Guid relationshipId, [FromBody] UpdateRelationshipCommand command)
    {
        command.RelationshipId = relationshipId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("relationships/{relationshipId}")]
    public async Task<IActionResult> DeleteRelationship([FromRoute] Guid relationshipId)
    {
        var command = new DeleteRelationshipCommand(relationshipId);
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return NoContent();
    }

    [HttpPost("{entityId}/gallery")]
    public async Task<IActionResult> AddGalleryImage(
        [FromRoute] Guid entityId,
        [FromForm] AddGalleryImageCommand command)
    {
        command.EntityId = entityId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPatch("gallery/{imageId}")]
    public async Task<IActionResult> UpdateGalleryImage(
        [FromRoute] Guid imageId,
        [FromBody] UpdateGalleryImageCommand command)
    {
        command.ImageId = imageId;
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("gallery/{imageId}")]
    public async Task<IActionResult> RemoveGalleryImage([FromRoute] Guid imageId)
    {
        var command = new RemoveGalleryImageCommand(imageId);
        var result = await mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return NoContent();
    }

    [HttpPatch("{entityId}/gallery/reorder")]
    public async Task<IActionResult> ReorderGalleryImages(
        [FromRoute] Guid entityId,
        [FromBody] ReorderGalleryImagesRequest request)
    {
        var command = new ReorderGalleryImagesCommand
        {
            EntityId = entityId,
            OrderedImageIds = request.OrderedImageIds
        };
        var result = await mediator.Send(command);
        if (!result) return BadRequest("Failed to reorder gallery images");
        return Ok();
    }
}
