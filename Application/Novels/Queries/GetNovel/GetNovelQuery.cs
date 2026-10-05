using Application.Chapters.Scheduling;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetNovel;

/// <summary>
/// A novel's page, by slug or by id. Both go through the same handler, so they return the same DTO under the same
/// rules: a draft is visible to its author only, a deleted novel to nobody.
/// </summary>
public class GetNovelQuery : IRequest<NovelsDTO>, IReadsNovel
{
    public GetNovelQuery(string novelSlug) => NovelSlug = novelSlug;

    public GetNovelQuery(Guid novelId) => NovelId = novelId;

    public string? NovelSlug { get; }
    public Guid? NovelId { get; }

    Guid? IReadsNovel.ReadNovelId => NovelId;
    string? IReadsNovel.ReadNovelSlug => NovelSlug;
}
