using Application.ReadingLists.DTOs;
using Domain.ReadingLists;
using Application.Common;

namespace Application.ReadingLists.Queries;

internal static class ReadingListPreviewMapping
{
    /// <summary>The web's new-post dialog lists up to 100 of the user's reading lists.</summary>
    public const int MaxPageSize = 100;

    public static (int PageNumber, int PageSize) ClampPage(int pageNumber, int pageSize) =>
        Paging.Clamp(pageNumber, pageSize, MaxPageSize);

    public static ReadingListPreviewDTO ToPreviewDto(this ReadingListSummary summary, bool isOwner, bool isFollowing) => new()
    {
        Id = summary.List.Id,
        Name = summary.List.Name,
        Description = summary.List.Description,
        CoverImageUrl = summary.List.CoverImageUrl,
        IsPublic = summary.List.IsPublic,
        NovelsCount = summary.VisibleNovelsCount,
        FollowersCount = summary.List.FollowersCount,
        UpdatedAt = summary.List.UpdatedAt,
        PreviewNovels = summary.PreviewNovels
            .Select(n => new NovelPreviewDTO
            {
                NovelId = n.NovelId,
                Slug = n.Slug,
                CoverImageUrl = n.CoverImageUrl,
                Title = n.Title
            })
            .ToList(),
        IsOwner = isOwner,
        IsFollowing = isFollowing
    };
}
