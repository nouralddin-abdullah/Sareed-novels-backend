using Application.Common;
using MediatR;

namespace Application.Search.Queries.SuggestNovels;

public record SuggestNovelsQuery(string Query, int MaxSuggestions = 5) : IRequest<List<NovelSuggestion>>;

public class NovelSuggestion
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string? CoverImageUrl { get; set; }
}
