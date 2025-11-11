namespace Application.Entities.Commands.ReorderArticles;

public class ReorderArticlesRequest
{
    public List<Guid> OrderedArticleIds { get; set; } = new();
}
