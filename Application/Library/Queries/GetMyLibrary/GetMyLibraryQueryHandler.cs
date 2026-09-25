using Application.Common;
using Application.Library.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Library;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Library.Queries.GetMyLibrary;

public class GetMyLibraryQueryHandler(
    ILogger<GetMyLibraryQueryHandler> logger,
    ILibraryRepository libraryRepository,
    IUserContext userContext) : IRequestHandler<GetMyLibraryQuery, PagedResult<ReadingProgressDTO>>
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<ReadingProgressDTO>> Handle(GetMyLibraryQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        logger.LogInformation("Getting library for user {UserId}, page {Page}", currentUser.Id, pageNumber);

        var (entries, totalCount) = await libraryRepository.GetUserLibraryAsync(currentUser.Id, pageNumber, pageSize);

        var dtos = entries.Select(entry =>
        {
            var resume = ReadingPosition.Resolve(entry.LastReadChapter, entry.PublishedChapters);
            if (resume.ChapterId != entry.LastReadChapter.Id)
            {
                logger.LogInformation(
                    "Chapter {ChapterId} that user {UserId} last read in novel {NovelId} is no longer published; resuming at {ResumeChapterId}",
                    entry.LastReadChapter.Id, currentUser.Id, entry.NovelId, resume.ChapterId);
            }

            return new ReadingProgressDTO
            {
                NovelId = entry.NovelId,
                Title = entry.Title,
                Slug = entry.Slug,
                CoverImageUrl = entry.CoverImageUrl,
                TotalChapters = resume.PublishedChapters,
                TotalAverageScore = entry.TotalAverageScore,
                TotalViews = entry.TotalViews,
                LastReadChapterId = resume.ChapterId,
                LastReadChapterNumber = resume.ChapterNumber,
                LastReadChapterTitle = resume.ChapterTitle,
                ProgressPercentage = resume.ProgressPercentage,
                LastReadAt = entry.LastReadAt,
                Author = new NovelAuthorDTO
                {
                    UserName = entry.AuthorUserName,
                    DisplayName = entry.AuthorDisplayName,
                    ProfilePhoto = entry.AuthorProfilePhoto
                }
            };
        }).ToList();

        return new PagedResult<ReadingProgressDTO>(dtos, totalCount, pageSize, pageNumber);
    }
}
