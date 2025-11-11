using MediatR;

namespace Application.Entities.Commands.ReorderArticles;

public class ReorderArticlesCommand : IRequest<bool>
{
    public Guid EntityId { get; set; }
    public List<Guid> OrderedArticleIds { get; set; } = new();
}
