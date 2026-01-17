using Application.Novels.DTOS;
using MediatR;

namespace Application.Competitions.Queries.GetMyEligibleNovels;

public class GetMyEligibleNovelsQuery : IRequest<List<EligibleNovelDto>>
{
    public Guid CompetitionId { get; set; }
}

public class EligibleNovelDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public string CoverImageUrl { get; set; } = default!;
    public int ChapterCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsAlreadyParticipating { get; set; }
}
