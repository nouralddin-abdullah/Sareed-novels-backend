using Application.Competitions.DTOs;
using Application.Common;
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
            ?? throw new ForbidException("سجّل الدخول للمشاركة في المسابقة", "NotSignedIn");

        var competition = await competitionRepository.GetByIdAsync(request.CompetitionId)
            ?? throw new NotFoundException("المسابقة غير موجودة", "CompetitionNotFound");

        // Check if competition is open for participation
        if (!competition.CanJoin())
        {
            throw new ForbidException("المشاركة في هذه المسابقة غير مفتوحة الآن", "CompetitionClosed");
        }

        // Get the novel
        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");

        // Verify ownership
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("يمكنك المشاركة في المسابقة برواياتك فقط", "NotOwner");
        }

        // Check if novel is already in this competition
        if (await participantRepository.IsNovelParticipatingAsync(request.CompetitionId, request.NovelId))
        {
            throw new ForbidException("هذه الرواية مشاركة في المسابقة بالفعل", "AlreadyParticipating");
        }

        // Validate novel eligibility - Age check
        if (competition.MaxNovelAgeDays.HasValue)
        {
            var maxAge = TimeSpan.FromDays(competition.MaxNovelAgeDays.Value);
            var novelAge = DateTime.UtcNow - novel.CreatedAt;
            if (novelAge > maxAge)
            {
                throw new ForbidException($"يُشترط للمشاركة ألا يتجاوز عمر الرواية {ArabicCount.DaysObject(competition.MaxNovelAgeDays.Value)}", "NovelTooOld");
            }
        }

        // Validate novel eligibility - Chapter count
        var chapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var publishedChapterCount = chapters.Count();
        if (publishedChapterCount < competition.MinChapters)
        {
            throw new ForbidException($"يلزم للمشاركة {ArabicCount.PublishedChapters(competition.MinChapters)} على الأقل (المنشور الآن: {publishedChapterCount})", "NotEnoughPublishedChapters");
        }

        // Validate novel is published
        if (novel.IsDraft || novel.IsDeleted)
        {
            throw new ForbidException("يمكن المشاركة في المسابقات بالروايات المنشورة فقط", "NovelNotPublished");
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
