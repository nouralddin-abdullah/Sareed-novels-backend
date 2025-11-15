using Application.Notifications.DTOs;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Notifications.Queries.GetComment;

public class GetCommentQueryHandler(
    ILogger<GetCommentQueryHandler> logger,
    ICommentsRepository commentsRepository,
    INotificationsRepository notificationsRepository,
    IChaptersRepository chaptersRepository,
    IChapterParagraphsRepository paragraphsRepository,
    INovelsRepository novelsRepository,
    IPostsRepository postsRepository,
    ICommentLikesRepository commentLikesRepository,
    IUserContext userContext,
    IMapper mapper) : IRequestHandler<GetCommentQuery, CommentDetailDto>
{
    public async Task<CommentDetailDto> Handle(GetCommentQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting comment {CommentId} with context", request.CommentId);

        var comment = await commentsRepository.GetCommentById(request.CommentId)
            ?? throw new NotFoundException("Comment not found");

        if (comment.IsDeleted)
        {
            throw new NotFoundException("Comment not found");
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

        // Get context information
        var context = new CommentLocationDto();
        var pageSize = 10; // Default page size

        if (comment.ChapterId.HasValue)
        {
            var chapter = await chaptersRepository.GetChapterById(comment.ChapterId.Value)
                ?? throw new NotFoundException("Chapter not found");
            
            var novel = await novelsRepository.GetOne(chapter.NovelId)
                ?? throw new NotFoundException("Novel not found");

            context.ChapterId = chapter.Id;
            context.ChapterTitle = chapter.Title;
            context.ChapterSlug = chapter.Slug;
            context.NovelId = novel.Id;
            context.NovelSlug = novel.Slug;
            context.NovelTitle = novel.Title;
            context.TotalComments = chapter.TotalCommentsCount;

            var pageNumber = await notificationsRepository.GetCommentPageNumber(
                chapter.Id, null, comment.Id, pageSize);
            context.PageNumber = pageNumber;
        }
        else if (comment.ParagraphId.HasValue)
        {
            // Paragraph comment - need to get chapter through paragraph
            var paragraph = await paragraphsRepository.GetParagraphById(comment.ParagraphId.Value)
                ?? throw new NotFoundException("Paragraph not found");
            
            var chapter = await chaptersRepository.GetChapterById(paragraph.ChapterId)
                ?? throw new NotFoundException("Chapter not found");
            
            var novel = await novelsRepository.GetOne(chapter.NovelId)
                ?? throw new NotFoundException("Novel not found");

            context.ChapterId = chapter.Id;
            context.ChapterTitle = chapter.Title;
            context.ChapterSlug = chapter.Slug;
            context.NovelId = novel.Id;
            context.NovelSlug = novel.Slug;
            context.NovelTitle = novel.Title;
            context.TotalComments = chapter.TotalCommentsCount;

            var pageNumber = await notificationsRepository.GetCommentPageNumber(
                chapter.Id, null, comment.Id, pageSize);
            context.PageNumber = pageNumber;
        }
        else if (comment.PostId.HasValue)
        {
            var post = await postsRepository.GetPostById(comment.PostId.Value)
                ?? throw new NotFoundException("Post not found");

            context.PostId = post.Id;
            context.TotalComments = post.CommentsCount;

            var pageNumber = await notificationsRepository.GetCommentPageNumber(
                null, post.Id, comment.Id, pageSize);
            context.PageNumber = pageNumber;
        }

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
            comment.Id, 1, 3, "oldest");
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
}
