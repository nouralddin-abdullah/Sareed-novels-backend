using Application.Chapters.DTOS;
using Application.Chapters.Paragraphs;
using Application.Chapters.Publishing;
using Application.Chapters.Scheduling;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterCommandHandler(
    ILogger<CreateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IUserContext userContext, 
    INovelsRepository novelsRepository, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    IServiceProvider serviceProvider,
    TimeProvider time) : IRequestHandler<CreateChapterCommand, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(CreateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Adding new chapter for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        
        if (novel.AuthorId != currentUser.Id) 
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        
        var now = time.GetUtcNow().UtcDateTime;
        // A schedule (#77) is for a draft, at a time to come: refused before anything is stored.
        if (ChapterSchedule.Refusal(request.PublishAt, request.Status, now) is { } refusal)
            throw new BadRequestException(refusal.Message, refusal.Code);
        var chapter = mapper.Map<Chapter>(request);
        chapter.PublishAt = ChapterSchedule.ToUtc(request.PublishAt);
        chapter.ChapterIndex = await chaptersRepository.GetNextChapterIndex(novel.Id);
        chapter.Id = Guid.NewGuid();
        chapter.Slug = Slugs.For(chapter.Id, request.Title);
        // Created now; created published, it also comes out now (#33). Its first revision (#75).
        chapter.CreatedAt = now;
        chapter.UpdatedAt = now;
        chapter.Revision = 1;
        chapter.SetStatus(request.Status, now);
        
        // The text as chapter format v1 stores it (#74), one row per paragraph, and its words (#77).
        var text = ChapterFormat.Parse(request.Content);
        var paragraphs = text
            .Select((paragraph, index) => ParagraphRows.New(chapter.Id, paragraph, index, now))
            .ToList();
        
        chapter.Paragraphs = paragraphs;
        chapter.ParagraphsCount = paragraphs.Count;
        chapter.WordsCount = ChapterWords.Count(text);
        
        var result = await chaptersRepository.CreateChapter(chapter);
        if (!result)
        {
            throw new InvalidOperationException("Failed to create the chapter");
        }

        // Created published, the chapter comes out now: the novel's last update moves to it and readers are told (#39).
        // A draft doesn't come out, and changes neither, until it is first published (UpdateChapterCommandHandler).
        var cameOut = chapter.Status == ChapterStatuses.Published;
        await novelsRepository.RefreshChapterCountAsync(novel.Id, lastUpdatedAt: cameOut ? chapter.PublishedAt : null);

        var locked = false;
        if (cameOut)
        {
            logger.LogInformation(
                "New Published chapter {ChapterId} created for novel {NovelId}, triggering sequence recalculation", 
                chapter.Id, novel.Id);
            
            await sequenceService.RecalculateSequencesForNovelAsync(novel.Id);
            
            // It locks in early access while the novel has it on (#94), and readers with the novel in their library are
            // told, as when a draft is published (none while the novel is hidden, #80).
            locked = await new ChapterStatusEffects(sequenceService, novelsRepository, serviceProvider, logger)
                .CameOutAsync(novel.Id, chapter);
        }

        var chapterDto = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        if (locked)
        {
            // Locked from when it came out, as stored: the answer says so, as the author's chapter does.
            chapter.EarlyAccessFrom = chapter.PublishedAt;
            var earlyAccess = await serviceProvider.GetRequiredService<IPrivilegeService>()
                .GetViewAsync(novel.Id, novel.AuthorId, viewerId: null);
            chapterDto.IsLocked = earlyAccess.IsLocked(chapter);
            chapterDto.UnlocksAt = earlyAccess.UnlocksAt(chapter);
        }
        
        logger.LogInformation("Chapter {ChapterId} created successfully with {ParagraphCount} paragraphs for novel {NovelId}", 
            chapter.Id, paragraphs.Count, novel.Id);
        
        return chapterDto;
    }
}
