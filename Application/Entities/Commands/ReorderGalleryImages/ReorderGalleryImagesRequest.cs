namespace Application.Entities.Commands.ReorderGalleryImages;

public class ReorderGalleryImagesRequest
{
    public List<Guid> OrderedImageIds { get; set; } = new();
}
