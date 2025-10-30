using Domain.Entities;

namespace Application.Novels.DTOS
{
    public class MyWorksDTO
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = default!;
        public string Slug { get; set; } = default!;
        public string CoverImageUrl { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime LastUpdatedAt { get; set; }
        public int TotalViews { get; set; }
        public int TotalAverageScore { get; set; }
        public int ChapterCount { get; set; }
        public bool IsDraft { get; set; } 
        public List<GenreSmallDto> GenresList { get; set; } = new List<GenreSmallDto>();
    }

    public class WorkDTO : MyWorksDTO
    {
        public string Summary { get; set; } = default!;
        public DateTime CreatedAt { get; set; }
    }
}
