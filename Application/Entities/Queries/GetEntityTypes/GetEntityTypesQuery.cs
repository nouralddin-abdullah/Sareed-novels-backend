using Application.Entities.DTOs;
using MediatR;

namespace Application.Entities.Queries.GetSections;

public class GetSectionsQuery(Guid novelId) : IRequest<SectionsResponseDTO>
{
    public Guid NovelId { get; set; } = novelId;
}
