using Application.Entities.DTOs;
using Application.Users;
using Domain.Entities;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Application.Entities.Queries.GetEntityById;

public class GetEntityByIdQueryHandler(
    ILogger<GetEntityByIdQueryHandler> logger,
    INovelEntityRepository entityRepository,
    IUserContext userContext) : IRequestHandler<GetEntityByIdQuery, EntityDTO?>
{
    public async Task<EntityDTO?> Handle(GetEntityByIdQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting entity {EntityId}", request.EntityId);

        var entity = await entityRepository.GetEntityByIdAsync(request.EntityId);
        if (entity == null) return null;

        // Check ownership using included Novel
        var currentUser = userContext.GetCurrentUser();
        var isOwner = currentUser != null && entity.Novel?.AuthorId == currentUser.Id;

        var attributes = new Dictionary<string, object>();
        try
        {
            if (!string.IsNullOrEmpty(entity.AttributesJson) && entity.AttributesJson != "{}")
            {
                attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(entity.AttributesJson) ?? new();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse attributes for entity {EntityId}", entity.Id);
        }

        return new EntityDTO
        {
            Id = entity.Id,
            NovelId = entity.NovelId,
            Section = entity.Section,
            Icon = entity.Icon,
            Name = entity.Name,
            ShortDescription = entity.ShortDescription,
            Description = entity.Description,
            Role = entity.Role,
            ImageUrl = entity.ImageUrl,
            Attributes = attributes,
            Articles = entity.Articles
                .OrderBy(a => a.OrderIndex)
                .Select(a => new EntityArticleDTO
                {
                    Id = a.Id,
                    Title = a.Title,
                    Content = a.Content,
                    OrderIndex = a.OrderIndex,
                    CreatedAt = a.CreatedAt
                }).ToList(),
            GalleryImages = entity.GalleryImages
                .OrderBy(g => g.OrderIndex)
                .Select(g => new EntityGalleryImageDTO
                {
                    Id = g.Id,
                    ImageUrl = g.ImageUrl,
                    Caption = g.Caption,
                    OrderIndex = g.OrderIndex,
                    CreatedAt = g.CreatedAt
                }).ToList(),
            Relationships = GetBidirectionalRelationships(entity),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            IsOwner = isOwner
        };
    }

    private static List<EntityRelationshipDTO> GetBidirectionalRelationships(NovelEntity entity)
    {
        var relationships = new List<EntityRelationshipDTO>();

        // Forward relationships (this entity is SOURCE)
        foreach (var r in entity.SourceRelationships)
        {
            relationships.Add(new EntityRelationshipDTO
            {
                Id = r.Id,
                TargetEntityId = r.TargetEntityId,
                TargetEntityName = r.TargetEntity?.Name ?? "",
                TargetEntityImage = r.TargetEntity?.ImageUrl,
                RelationType = r.RelationType,
                Label = r.Label,
                ReverseLabel = r.ReverseLabel,
                Description = r.Description
            });
        }

        // Reverse relationships (this entity is TARGET)
        foreach (var r in entity.TargetRelationships)
        {
            // Only add if reverseLabel exists (null means one-way relationship)
            if (!string.IsNullOrEmpty(r.ReverseLabel))
            {
                relationships.Add(new EntityRelationshipDTO
                {
                    Id = r.Id,
                    TargetEntityId = r.SourceEntityId,  // Swapped: show the other entity
                    TargetEntityName = r.SourceEntity?.Name ?? "",
                    TargetEntityImage = r.SourceEntity?.ImageUrl,
                    RelationType = r.RelationType,
                    Label = r.ReverseLabel,  // Use reverse label
                    ReverseLabel = r.Label,  // Show original label as reverse
                    Description = r.Description
                });
            }
        }

        return relationships;
    }
}
