namespace Domain.Entities;

public class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<NovelGenre> NovelGenres { get; set; } = new List<NovelGenre>();
}
