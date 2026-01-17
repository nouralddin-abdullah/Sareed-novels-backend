using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Queries.GetMyEligibleNovels;

public class GetMyEligibleNovelsQueryHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository,
    IUserContext userContext) : IRequestHandler<GetMyEligibleNovelsQuery, List<EligibleNovelDto>>
{
    public async Task<List<EligibleNovelDto>> Handle(GetMyEligibleNovelsQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("You must be logged in");

        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("Competition not found");

        // Get all author's novels (published and drafts)
        var (myNovels, _) = await novelsRepository.GetWorks(currentUser.Id, 1, 1000);
        var publishedNovels = myNovels.Where(n => n != null && !n.IsDraft && !n.IsDeleted).ToList();

        var eligibleNovels = new List<EligibleNovelDto>();

        foreach (var novel in publishedNovels)
        {
            if (novel == null) continue;

            // Check age criteria
            if (competition.MaxNovelAgeDays.HasValue)
            {
                var maxAge = TimeSpan.FromDays(competition.MaxNovelAgeDays.Value);
                var novelAge = DateTime.UtcNow - novel.CreatedAt;
                if (novelAge > maxAge)
                {
                    continue; // Too old, skip
                }
            }

            // Check chapter count criteria
            var chapters = await chaptersRepository.GetChaptersReaderView(novel.Id);
            var publishedChapterCount = chapters.Count();
            if (publishedChapterCount < competition.MinChapters)
            {
                continue; // Not enough chapters, skip
            }

            // Check if already participating
            var isParticipating = await participantRepository.IsNovelParticipatingAsync(request.CompetitionId, novel.Id);

            eligibleNovels.Add(new EligibleNovelDto
            {
                Id = novel.Id,
                Title = novel.Title,
                Slug = novel.Slug,
                CoverImageUrl = novel.CoverImageUrl,
                ChapterCount = publishedChapterCount,
                CreatedAt = novel.CreatedAt,
                IsAlreadyParticipating = isParticipating
            });
        }

        return eligibleNovels;
    }
}
