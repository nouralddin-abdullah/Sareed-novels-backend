using Domain.Entities;

namespace Application.Novels.DTOS
{
    public class MyWorksDTO
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = default!;
        public string Slug { get; set; } = default!;
        public string Summary { get; set; } = default!;
        public string CoverImageUrl { get; set; } = default!;
        public string Status { get; set; } = default!;
        public DateTime LastUpdatedAt { get; set; }
        public int TotalViews { get; set; }
        /// <summary>The average rating out of 5, with its fraction (3.75), like the other novel DTOs (#25: it was a whole number).</summary>
        public decimal TotalAverageScore { get; set; }
        public int ChapterCount { get; set; }

        /// <summary>
        /// The words of all the novel's chapters, drafts included (#77: only its author sees it); 0 without chapters. Null
        /// while one of its chapters, from before word counts, isn't counted yet (until the startup backfill reaches it).
        /// </summary>
        public int? WordsCount { get; set; }

        public bool IsDraft { get; set; } 
        public List<GenreSmallDto> GenresList { get; set; } = new List<GenreSmallDto>();
    }

    public class WorkDTO : MyWorksDTO
    {
        public DateTime CreatedAt { get; set; }
    }
}
