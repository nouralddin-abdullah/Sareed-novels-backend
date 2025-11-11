using Application.Entities.DTOs;
using MediatR;

namespace Application.Entities.Queries.GetEntityById;

public class GetEntityByIdQuery(Guid entityId) : IRequest<EntityDTO?>
{
    public Guid EntityId { get; set; } = entityId;
}
