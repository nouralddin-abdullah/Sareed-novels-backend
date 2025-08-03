using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetNovel;

public class GetNovelQuery(string novelSlug) : IRequest<NovelsDTO>
{
    public string NovelSlug { get; set; } = novelSlug;
}
