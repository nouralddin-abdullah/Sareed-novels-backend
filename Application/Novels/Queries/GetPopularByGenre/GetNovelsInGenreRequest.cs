namespace Application.Novels.Queries.GetPopularByGenre;

public class GetNovelsInGenreRequest
{
    public int? PageSize { get; set; }
    public int? PageNumber { get; set; }
    public string? Sorting { get; set; }
    public bool? IsCompleted { get; set; }
}
