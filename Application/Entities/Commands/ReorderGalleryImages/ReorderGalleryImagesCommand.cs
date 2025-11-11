using MediatR;

namespace Application.Entities.Commands.ReorderGalleryImages;

public class ReorderGalleryImagesCommand : IRequest<bool>
{
    public Guid EntityId { get; set; }
    public List<Guid> OrderedImageIds { get; set; } = new();
}
