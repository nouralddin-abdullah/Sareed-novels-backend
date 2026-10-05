using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Application.Chapters.Publishing;
using Application.Chapters.Scheduling;
using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
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
    TimeProvider time) : IRequestHandler<UpdateChapterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updating chapter {ChapterId} of novel {NovelId}", request.ChapterId, request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");
        
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");

        // The chapter must belong to the novel the caller owns; otherwise any author could edit any chapter.
        if (chapter.NovelId != novel.Id) throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        // A schedule (#77) is for a draft, at a time to come: refused before anything is saved. Null cancels it.
        if (request.SetsSchedule
            && ChapterSchedule.Refusal(request.PublishAt, request.Status ?? chapter.Status, time.GetUtcNow().UtcDateTime) is { } refusal)
        {
            return new OperationResult { Success = false, Code = refusal.Code, Message = refusal.Message };
        }
        
        // Paragraphs first, while the chapter entity is unchanged: the paragraph transaction then holds the chapter
        // row only for its counter update at the end, not from its first write, while readers may be commenting.
        if (!string.IsNullOrEmpty(request.Content))
        {
            chapter.ParagraphsCount = await SaveParagraphs(chapter.Id, request.Content);
            chapter.WordsCount = ChapterWords.Count(await paragraphsRepository.GetChapterParagraphs(chapter.Id)); // #77, as stored
        }
        
        if (request.Title != null)
        {
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
            chapter.SetStatus(request.Status, time.GetUtcNow().UtcDateTime);
        }
        if (request.SetsSchedule)
        {
            chapter.PublishAt = ChapterSchedule.ToUtc(request.PublishAt); // a publish clears it (UpdateChapter, #77)
        }
        
        // A save without a status leaves the stored one (#77: the schedule may have published the chapter meanwhile).
        var saved = await chaptersRepository.UpdateChapter(chapter, withStatus: request.Status != null);

        // Published or unpublished by this save: sequences, chapter count, last update, privileges and readers'
        // notifications, as when the schedule publishes a chapter (#77). Only the save that changed the stored status.
        await new ChapterStatusEffects(sequenceService, novelsRepository, serviceProvider, logger).ApplyAsync(novel.Id, chapter, saved);

        if (saved.Saved)
        {
            return new OperationResult
            {
                Success = true,
                Message = "حُفظ الفصل"
            };
        }
        
        return new OperationResult
        {
            Success = false,
            Code = "OperationFailed",
            Message = "تعذّر حفظ الفصل. حاول مرة أخرى."
        };
    }
    
    /// <summary>
    /// Replaces the chapter's paragraphs with the edited content and returns how many there are now. A paragraph
    /// whose words are unchanged (<see cref="ParagraphText.VisibleText"/>) keeps its id and its comments, wherever
    /// it moved and whatever its formatting; a changed or deleted paragraph goes, and its comments with it.
    /// </summary>
    private async Task<int> SaveParagraphs(Guid chapterId, string content)
    {
        var stopwatch = Stopwatch.StartNew();
        var saved = await paragraphsRepository.GetChapterParagraphs(chapterId);
        var editedTexts = ParagraphText.Split(content);
        var match = ParagraphMatcher.Match(saved.Select(p => p.Content).ToList(), editedTexts);

        var now = DateTime.UtcNow;
        var reformatted = 0;
        var paragraphs = new List<ChapterParagraph>(editedTexts.Count);
        for (var index = 0; index < editedTexts.Count; index++)
        {
            var text = editedTexts[index];
            var savedIndex = match.SavedIndexByEdited[index];
            if (savedIndex < 0)
            {
                paragraphs.Add(new ChapterParagraph
                {
                    Id = Guid.NewGuid(),
                    ChapterId = chapterId,
                    Content = text,
                    ContentHash = ParagraphText.Hash(text),
                    OrderIndex = index,
                    ContentType = "text",
                    CreatedAt = now,
                    CommentsCount = 0
                });
                continue;
            }

            var paragraph = saved[savedIndex];
            var changed = false;
            if (paragraph.Content != text)
            {
                // Same words in new markup (bold, italic, line breaks, spacing): readers get the new markup.
                paragraph.Content = text;
                paragraph.ContentHash = ParagraphText.Hash(text);
                reformatted++;
                changed = true;
            }

            if (paragraph.OrderIndex != index)
            {
                paragraph.OrderIndex = index;
                changed = true;
            }

            if (changed)
            {
                paragraph.UpdatedAt = now;
            }

            paragraphs.Add(paragraph);
        }

        var removed = match.RemovedSaved.Select(i => saved[i]).ToList();
        var deleted = await paragraphsRepository.SaveEditedParagraphs(chapterId, paragraphs, removed);

        logger.Log(deleted.Visible > 0 ? LogLevel.Warning : LogLevel.Information,
            "Chapter {ChapterId} paragraphs saved: {Kept} kept, {Moved} moved, {Created} new, {Removed} removed " +
            "({Reformatted} kept with new formatting); {CommentsDeleted} comments deleted with the removed paragraphs " +
            "({VisibleCommentsDeleted} visible to readers); {ElapsedMs} ms",
            chapterId, match.Kept, match.Moved, match.Created, match.Removed, reformatted,
            deleted.Comments, deleted.Visible, stopwatch.ElapsedMilliseconds);

        return paragraphs.Count;
    }
}
