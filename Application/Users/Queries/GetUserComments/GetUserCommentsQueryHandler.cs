using Application.Chapters;
using Application.Common;
using Application.Services;
using Application.Users.DTOS;
using Domain.Entities;
using Domain.Profiles;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Users.Queries.GetUserComments;

public class GetUserCommentsQueryHandler(
    ILogger<GetUserCommentsQueryHandler> logger,
    UserManager<User> userManager,
    IUsersRepository usersRepository,
    IUserBlocksRepository blocksRepository,
    IProfileListsRepository profileLists,
    ICommentLikesRepository commentLikesRepository,
    IPrivilegeService privilegeService,
    IUserContext userContext) : IRequestHandler<GetUserCommentsQuery, PagedResult<ProfileCommentDTO>>
{
    public async Task<PagedResult<ProfileCommentDTO>> Handle(GetUserCommentsQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize);
        // The member the profile shows, or its 404.
        var member = await ProfileLookup.FindMemberAsync(userManager, usersRepository, request.UserName, cancellationToken);

        // Refused to anyone but the member when they hid it (#61), before the block rule.
        var viewer = userContext.GetCurrentUser();
        HiddenLists.EnsureCommentsShownTo(member, viewer?.Id);

        // As the member's posts: empty, not refused, when the viewer and the member blocked each other.
        if (await Blocks.EitherWayAsync(blocksRepository, viewer?.Id, member.Id, cancellationToken))
        {
            return new PagedResult<ProfileCommentDTO>([], 0, pageSize, pageNumber);
        }

        var (comments, totalCount) = await profileLists.GetCommentsAsync(member.Id, viewer?.Id, pageNumber, pageSize,
            cancellationToken);
        // What the viewer liked among the listed comments and the comments they answer (#67), in one query.
        var liked = viewer != null && comments.Count > 0
            ? await commentLikesRepository.GetUserLikedCommentIds(viewer.Id, CommentsShown(comments))
            : [];
        var readChapters = await ChaptersTheViewerReads(comments, viewer?.Id);

        logger.LogInformation("Listed {Count} of {Total} comments of user {UserId}", comments.Count, totalCount, member.Id);
        return new PagedResult<ProfileCommentDTO>(
            comments.Select(c => c.ToDto(liked, readChapters.Contains(c.Chapter.Id))).ToList(),
            totalCount, pageSize, pageNumber);
    }

    /// <summary>
    /// The comments a page shows: its items and the comments its replies answer, each once (a reply to the member's own
    /// comment may answer one of the items).
    /// </summary>
    private static IEnumerable<Guid> CommentsShown(IReadOnlyList<ProfileComment> comments) =>
        comments
            .SelectMany(c => c.ParentComment is { } parent ? new[] { c.Id, parent.Id } : new[] { c.Id })
            .Distinct();

    /// <summary>
    /// The chapters of the page's paragraph comments whose text the reader would show the viewer, so their paragraphs
    /// may be quoted (#60), decided once for each chapter. Every listed chapter is one readers can open (published, in
    /// a novel readers can open: the list's own rule), so what is left is early access (ChapterAccess).
    /// </summary>
    private async Task<HashSet<Guid>> ChaptersTheViewerReads(IReadOnlyList<ProfileComment> comments, string? viewerId)
    {
        var read = new HashSet<Guid>();
        var chapters = comments
            .Where(c => c.Paragraph != null)
            .Select(c => (ChapterId: c.Chapter.Id, c.Novel.AuthorId))
            .Distinct();
        foreach (var (chapterId, authorId) in chapters)
        {
            if (await ChapterAccess.IsUnlockedForAsync(privilegeService, authorId, chapterId, viewerId))
            {
                read.Add(chapterId);
            }
        }

        return read;
    }
}
