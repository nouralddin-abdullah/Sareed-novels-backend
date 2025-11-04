namespace Application.Comments.Queries.GetChapterComments;

public class GetChapterCommentsRequest
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string Sorting { get; set; } = "recent";
}
