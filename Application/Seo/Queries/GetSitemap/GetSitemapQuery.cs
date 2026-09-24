using Domain.Repositories;
using Domain.Seo;
using MediatR;

namespace Application.Seo.Queries.GetSitemap;

public record GetSitemapQuery : IRequest<List<NovelSitemapEntry>>;

public class GetSitemapQueryHandler(INovelsRepository novelsRepository) : IRequestHandler<GetSitemapQuery, List<NovelSitemapEntry>>
{
    public Task<List<NovelSitemapEntry>> Handle(GetSitemapQuery request, CancellationToken cancellationToken) =>
        novelsRepository.GetSitemapEntriesAsync(cancellationToken);
}
