using Application.Competitions.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Competitions.Commands.JoinCompetition;

public class JoinCompetitionCommandHandler(
    ICompetitionRepository competitionRepository,
    ICompetitionParticipantRepository participantRepository,
    INovelsRepository novelsRepository,
    IChaptersRepository chaptersRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<JoinCompetitionCommand, CompetitionParticipantDto>
{
    public async Task<CompetitionParticipantDto> Handle(JoinCompetitionCommand request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("You must be logged in to join a competition");

        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("Competition not found");

        // Check if competition is open for participation
        if (!competition.CanJoin())
        {
            throw new ForbidException("This competition is not currently accepting participants");
        }

        // Get the novel
        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("Novel not found");

        // Verify ownership
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("You can only enter your own novels into a competition");
        }

        // Check if novel is already in this competition
        if (await participantRepository.IsNovelParticipatingAsync(request.CompetitionId, request.NovelId))
        {
            throw new ForbidException("This novel is already participating in this competition");
        }

        // Validate novel eligibility - Age check
        if (competition.MaxNovelAgeDays.HasValue)
        {
            var maxAge = TimeSpan.FromDays(competition.MaxNovelAgeDays.Value);
            var novelAge = DateTime.UtcNow - novel.CreatedAt;
            if (novelAge > maxAge)
            {
                throw new ForbidException($"Novel must be created within the last {competition.MaxNovelAgeDays} days to participate");
            }
        }

        // Validate novel eligibility - Chapter count
        var chapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var publishedChapterCount = chapters.Count();
        if (publishedChapterCount < competition.MinChapters)
        {
            throw new ForbidException($"Novel must have at least {competition.MinChapters} published chapters to participate. Current: {publishedChapterCount}");
        }

        // Validate novel is published
        if (novel.IsDraft || novel.IsDeleted)
        {
            throw new ForbidException("Only published novels can participate in competitions");
        }

        // Create participant entry
        var participant = new CompetitionParticipant
        {
            Id = Guid.NewGuid(),
            CompetitionId = request.CompetitionId,
            NovelId = request.NovelId,
            JoinedAt = DateTime.UtcNow,
            ViewsAtJoin = novel.TotalViews,
            CurrentPoints = 0,
            ExtraPoints = 0,
            CurrentRank = 0
        };

        await participantRepository.CreateAsync(participant);

        // Reload with navigation properties for mapping
        var savedParticipant = await participantRepository.GetByIdAsync(participant.Id);
        return mapper.Map<CompetitionParticipantDto>(savedParticipant);
    }
}
