using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Application.Chapters.Publishing;
using Application.Chapters.Scheduling;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommandHandler(
    ILogger<UpdateChapterCommandHandler> logger, 
    IChaptersRepository chaptersRepository, 
    IChapterParagraphsRepository paragraphsRepository, 
    INovelsRepository novelsRepository, 
    IUserContext userContext, 
    IMapper mapper,
    IChapterSequenceService sequenceService,
    IServiceProvider serviceProvider,
    TimeProvider time) : IRequestHandler<UpdateChapterCommand, UpdateChapterResult>
{
    public async Task<UpdateChapterResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating chapter {ChapterId} of novel {NovelId}", request.ChapterId, request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");

        // The chapter must belong to the novel the caller owns; otherwise any author could edit any chapter.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        // The text as chapter format v1 stores it (#74), parsed before the chapter's text is held. A save with neither
        // a title nor text changes the status or the schedule alone (#75, #77).
        var edited = string.IsNullOrEmpty(request.Content) ? null : ChapterFormat.Parse(request.Content);
        var savesText = request.Title != null || edited != null;

        // One save of a chapter at a time, from any device (and the format maintenance, and the scheduled publish, #77):
        // the chapter is read again inside the edit, so the revision checked here is the one this save writes over (#75),
        // and the status and schedule are the ones it changes.
        await using var edit = await paragraphsRepository.BeginEditAsync(chapter.Id);
        if (!await chaptersRepository.ReloadAsync(chapter))
        {
            throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        }

        // A copy older than the chapter mustn't overwrite newer text (#75): the editor reloads it first. Nothing is saved.
        if (savesText && request.BaseRevision is { } baseRevision && baseRevision != chapter.Revision)
        {
            throw new ChapterChangedException(chapter.Revision);
        }

        // A schedule (#77) is for a draft, at a time to come, as the chapter is now (the schedule may have published it
        // meanwhile): refused before anything is saved. Null cancels it, but a chapter that has come out and stays out
        // has no schedule left to cancel (#88): the app that still shows it as a scheduled draft is told it came out.
        var now = time.GetUtcNow().UtcDateTime;
        if (request.SetsSchedule && ChapterSchedule.SaveRefusal(request.PublishAt, chapter.Status, request.Status, now) is { } refusal)
        {
            throw new BadRequestException(refusal.Message, refusal.Code);
        }

        var match = edited is null ? null : ParagraphMatcher.Match(edit.Paragraphs.Select(ParagraphRows.Read).ToList(), edited);
        if (request.DryRun)
        {
            // Everything checked and matched; disposing the edit lets it go with nothing written.
            return new UpdateChapterResult
            {
                Success = true, Message = "لم يُحفظ شيء", Revision = chapter.Revision,
                Preview = await Preview(edit, match)
            };
        }

        // The status as stored before this save: the effects of a publish or unpublish follow only a change of it.
        var statusBefore = chapter.Status;
        
        // Paragraphs first, while the chapter entity is unchanged: the edit then holds the chapter row only from its
        // updates at the end, not from its first write, while readers may be commenting.
        var textChanged = false;
        if (edited != null)
        {
            (chapter.ParagraphsCount, textChanged) = await SaveParagraphs(edit, chapter.Id, edited, match!, now);
            // The text is its paragraphs now; a copy left in the legacy Chapters.Content column is stale.
            chapter.Content = null;
            // Its words (#77): the paragraphs just written are the edited ones, and the count is saved with them.
            chapter.WordsCount = ChapterWords.Count(edited);
        }
        
        if (request.Title != null)
        {
            textChanged |= request.Title != chapter.Title;
            // The editor resends the unchanged title on every save; only a real rename may change the slug.
            var newSlug = Slugs.For(chapter.Id, request.Title);
            if (newSlug != Slugs.For(chapter.Id, chapter.Title))
            {
                chapter.Slug = newSlug;
            }
        }
        
        // Update basic fields, and the status through SetStatus: publishing a draft stamps when it comes out, the first
        // time only (#33). UpdateChapter stores that stamp only while the chapter has none, and says whether this save
        // is the one that brought the chapter out (#39).
        mapper.Map(request, chapter);
        if (request.Status != null)
        {
            chapter.SetStatus(request.Status, now);
        }

        // The draft's schedule (#77), when this save sets or cancels it; a save that doesn't keeps it. Publishing the
        // chapter cleared it (SetStatus).
        if (request.SetsSchedule)
        {
            chapter.PublishAt = ChapterSchedule.ToUtc(request.PublishAt);
        }

        // The revision counts the saves that change the title or the text; a change of status or schedule alone, or the
        // title and text sent back as they are, leaves it (#75). Saved in the edit, over the revision read in it.
        if (textChanged)
        {
            chapter.Revision++;
        }

        chapter.UpdatedAt = now;
        
        var saved = await chaptersRepository.UpdateChapter(chapter);
        if (!saved.Saved)
        {
            return new UpdateChapterResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "تعذّر حفظ الفصل. حاول مرة أخرى."
            };
        }

        // The chapter as this save stored it, with the chapter still held (#88): its revision, and its status and
        // schedule, which the scheduler may have changed just before this save read it, so the app shows them without
        // loading it.
        var answer = new UpdateChapterResult
        {
            Success = true,
            Message = "حُفظ الفصل",
            Revision = chapter.Revision,
            Status = chapter.Status,
            PublishAt = chapter.PublishAt is { } publishAt ? DateTime.SpecifyKind(publishAt, DateTimeKind.Utc) : null
        };

        await edit.CommitAsync();

        // Published or unpublished by this save: sequences, chapter count, last update, privileges and readers'
        // notifications, as when the schedule publishes a chapter (#77).
        await new ChapterStatusEffects(sequenceService, novelsRepository, serviceProvider, logger)
            .ApplyAsync(novel.Id, chapter, statusBefore, saved);

        return answer;
    }

    /// <summary>
    /// What saving the match would delete (#75, a dry run): the saved paragraphs it removes, in order, with the comments
    /// that would go with each.
    /// </summary>
    private static async Task<ChapterSavePreview> Preview(IChapterTextEdit edit, ParagraphMatch? match)
    {
        var removed = match?.RemovedSaved.Select(i => edit.Paragraphs[i]).ToList() ?? [];
        var comments = await edit.CountCommentsToDeleteAsync(removed.Select(p => p.Id).ToList());
        return new ChapterSavePreview
        {
            ParagraphsRemoved = removed.Count,
            CommentsDeleted = comments.Values.Sum(),
            Removed = removed.Select(p => new RemovedParagraphPreview { ParagraphId = p.Id, CommentsCount = comments[p.Id] }).ToList()
        };
    }
    
    /// <summary>
    /// Replaces the chapter's paragraphs with the edited ones, in chapter format v1 (#74), as <paramref name="match"/>
    /// paired them, and returns how many there are now and whether anything changed. A paragraph whose words are
    /// unchanged (<see cref="FormattedParagraph.MatchKey"/>) keeps its id and its comments, wherever it moved and
    /// whatever its kind or formatting, which are updated; a changed or deleted paragraph goes, and its comments with it.
    /// </summary>
    private async Task<(int Count, bool Changed)> SaveParagraphs(
        IChapterTextEdit edit, Guid chapterId, IReadOnlyList<FormattedParagraph> edited, ParagraphMatch match, DateTime now)
    {
        var stopwatch = Stopwatch.StartNew();
        var saved = edit.Paragraphs;
        var reformatted = 0;
        var reordered = false;
        var paragraphs = new List<ChapterParagraph>(edited.Count);
        for (var index = 0; index < edited.Count; index++)
        {
            var savedIndex = match.SavedIndexByEdited[index];
            if (savedIndex < 0)
            {
                paragraphs.Add(ParagraphRows.New(chapterId, edited[index], index, now));
                continue;
            }

            var paragraph = saved[savedIndex];
            var changed = false;
            // Same words in another kind or markup (center, bold, line breaks, spacing): readers get the new one.
            if (ParagraphRows.Store(paragraph, edited[index]))
            {
                reformatted++;
                changed = true;
            }

            if (paragraph.OrderIndex != index)
            {
                paragraph.OrderIndex = index;
                reordered = true;
                changed = true;
            }

            if (changed)
            {
                paragraph.UpdatedAt = now;
            }

            paragraphs.Add(paragraph);
        }

        var removed = match.RemovedSaved.Select(i => saved[i]).ToList();
        var deleted = await edit.SaveAsync(paragraphs, removed);

        logger.Log(deleted.Visible > 0 ? LogLevel.Warning : LogLevel.Information,
            "Chapter {ChapterId} paragraphs saved: {Kept} kept, {Moved} moved, {Created} new, {Removed} removed " +
            "({Reformatted} kept with new formatting); {CommentsDeleted} comments deleted with the removed paragraphs " +
            "({VisibleCommentsDeleted} visible to readers); {ElapsedMs} ms",
            chapterId, match.Kept, match.Moved, match.Created, match.Removed, reformatted,
            deleted.Comments, deleted.Visible, stopwatch.ElapsedMilliseconds);

        return (paragraphs.Count, match.Created > 0 || match.Removed > 0 || reformatted > 0 || reordered);
    }
}
