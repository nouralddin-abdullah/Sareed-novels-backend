namespace Application.Entities.DTOs;

public class SectionSummaryDTO
{
    public Guid? Id { get; set; } // ID of the section placeholder entity (for edit/delete)
    public string Name { get; set; } = default!;
    public string? Icon { get; set; }
}

public class SectionsResponseDTO
{
    public List<SectionSummaryDTO> Sections { get; set; } = new();
    public bool IsOwner { get; set; }
}
