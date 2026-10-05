using Application.Chapters;
using Application.Comments;
using Application.Common;
using Application.Notifications.DTOs;
using Application.Posts;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Queries.GetComment;

public class GetCommentQueryHandler(
    ILogger<GetCommentQueryHandler> logger,
    ICommentsRepository commentsRepository,
    IChaptersRepository chaptersRepository,
    IChapterParagraphsRepository paragraphsRepository,
    INovelsRepository novelsRepository,
    IPostsRepository postsRepository,
    ICommentLikesRepository commentLikesRepository,
    IUserBlocksRepository blocksRepository,
    IPrivilegeService privilegeService,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetCommentQuery, CommentDetailDto>
{
    public async Task<CommentDetailDto> Handle(GetCommentQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comment {CommentId} with context", request.CommentId);

        var comment = await commentsRepository.GetCommentById(request.CommentId)
            ?? throw new NotFoundException("هذا التعليق لم يعد موجودًا", "CommentNotFound");

        if (comment.IsDeleted)
        {
            throw new NotFoundException("هذا التعليق لم يعد موجودًا", "CommentNotFound");
        }

        var commentDto = mapper.Map<CommentDto>(comment);
        
        // Check if current user liked the comment
        var currentUser = userContext.GetCurrentUser();
        if (currentUser != null)
        {
            var likedCommentIds = await commentLikesRepository.GetUserLikedCommentIds(
                currentUser.Id, 
                new[] { comment.Id });
            commentDto.IsLikedByCurrentUser = likedCommentIds.Contains(comment.Id);
        }

        // Where the comment is: its chapter (through its paragraph, for a paragraph comment) and novel, or its post.
        // A reply is stored at its parent's place, so it resolves the same way.
        var context = new CommentLocationDto { ParentCommentId = comment.ParentCommentId };

        if (comment.ParagraphId.HasValue)
        {
            var paragraph = await paragraphsRepository.GetParagraphById(comment.ParagraphId.Value)
                ?? throw ParagraphGone.Exception();

            var (chapter, novel) = await SetChapter(context, paragraph.ChapterId);
            context.ParagraphId = paragraph.Id;
            context.ParagraphOrderIndex = paragraph.OrderIndex;
            // The excerpt is chapter text, so only for someone the reader would show this chapter to.
            if (await ChapterAccess.ShowsTextAsync(privilegeService, novel, chapter, currentUser?.Id))
            {
                context.ParagraphExcerpt = ParagraphExcerpt.Of(paragraph.Content, paragraph.ContentType, paragraph.Caption);
            }
        }
        else if (comment.ChapterId.HasValue)
        {
            await SetChapter(context, comment.ChapterId.Value);
        }
        else if (comment.PostId.HasValue)
        {
            var post = await postsRepository.GetPostById(comment.PostId.Value)
                ?? throw new NotFoundException("هذا المنشور لم يعد موجودًا", "PostNotFound");
            // To someone the post's author blocked, the post is unavailable, and so are its comments (PostBlocks).
            await PostBlocks.EnsureNotBlockedByAuthorAsync(blocksRepository, post.UserId, currentUser?.Id,
                cancellationToken);

            context.PostId = post.Id;
            context.TotalComments = post.CommentsCount;
        }

        // Within the list that shows the comment: its paragraph's, chapter's or post's, or for a reply its thread.
        var (_, pageSize) = Paging.Clamp(1, request.PageSize);
        context.PageNumber = await commentsRepository.CountCommentsAheadAsync(comment, currentUser?.Id) / pageSize + 1;

        // Get parent comment if this is a reply
        CommentDto? parentCommentDto = null;
        if (comment.ParentCommentId.HasValue)
        {
            var parentComment = await commentsRepository.GetCommentById(comment.ParentCommentId.Value);
            if (parentComment != null && !parentComment.IsDeleted)
            {
                parentCommentDto = mapper.Map<CommentDto>(parentComment);
            }
        }

        // Get first few replies
        var replies = new List<CommentReplyDto>();
        var (commentReplies, _) = await commentsRepository.GetCommentReplies(
            comment.Id, 1, 3, "oldest", currentUser?.Id);
        replies = mapper.Map<List<CommentReplyDto>>(commentReplies);

        if (currentUser != null && replies.Any())
        {
            var replyIds = replies.Select(r => Guid.Parse(r.Id.ToString()));
            var likedReplyIds = await commentLikesRepository.GetUserLikedCommentIds(
                currentUser.Id, replyIds);
            
            foreach (var reply in replies)
            {
                reply.IsLikedByCurrentUser = likedReplyIds.Contains(reply.Id);
            }
        }

        return new CommentDetailDto
        {
            Comment = commentDto,
            Context = context,
            ParentComment = parentCommentDto,
            Replies = replies
        };
    }

    private async Task<(Chapter Chapter, Novel Novel)> SetChapter(CommentLocationDto context, Guid chapterId)
    {
        var chapter = await chaptersRepository.GetChapterById(chapterId)
            ?? throw new NotFoundException("الفصل غير موجود", "ChapterNotFound");

        var novel = await novelsRepository.GetOne(chapter.NovelId)
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");

        context.ChapterId = chapter.Id;
        context.ChapterTitle = chapter.Title;
        context.ChapterSlug = chapter.Slug;
        context.NovelId = novel.Id;
        context.NovelSlug = novel.Slug;
        context.NovelTitle = novel.Title;
        context.TotalComments = chapter.TotalCommentsCount;
        return (chapter, novel);
    }
}
