namespace Domain.Entities;

public class Novel
{
    public Guid Id { get; set; }
    public string AuthorId { get; set; } = default!;
    public User Owner { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public string Status { get; set; } = "Ongoing";
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public int TotalViews { get; set; }
}
