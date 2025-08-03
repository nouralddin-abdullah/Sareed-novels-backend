using Domain.Constants;

namespace Application.Novels.Commands.UpdateNovel
{
    public class UpdateNovelCommandRequest
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Status { get; set; }
        public List<int>? GenreIds { get; set; }
    }
}
