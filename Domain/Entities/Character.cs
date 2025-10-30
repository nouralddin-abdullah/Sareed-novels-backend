namespace Domain.Entities;

public class Character
{
    public Guid Id { get; set; }
    public Guid NovelId { get; set; }
    public Novel Novel { get; set; } = default!;
    public string CharacterName { get; set; } = default!;
    public string CharacterDescription { get; set; } = default!;
    public int CharacterAge { get; set; } = default!;
    public string CharacterImageUrl { get; set; } = default!;

}
