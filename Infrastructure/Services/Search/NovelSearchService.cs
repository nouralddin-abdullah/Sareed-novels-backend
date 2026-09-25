using Application.Common;
using Application.Search.DTOs;
using Application.Services;
using Domain.Search;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Services.Search;

/// <summary>
/// Novel search straight from SQL. Every query word must appear in the normalized title (Novel.SearchTitle);
/// relevance is exact title > title starts with the query > a word starts with it > contains it, then popularity.
/// Like the sitemap and new arrivals, only novels a reader can open are listed: not a draft, and at least one
/// published chapter.
/// </summary>
public class NovelSearchService(ApplicationDbContext dbContext) : INovelSearchService
{
    private const string Published = "Published";
    internal const int MaxPageSize = 50;
    // Keeps (page - 1) * size inside int: a huge page number used to overflow into a negative OFFSET and a 500.
    internal const int MaxPageNumber = int.MaxValue / MaxPageSize;

    public async Task<PagedResult<NovelSearchResult>> SearchNovelsAsync(
        SearchNovelsRequest request,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Clamp(request.PageNumber, 1, MaxPageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var tokens = SearchText.Tokens(request.Query);
        if (SearchText.HasNothingSearchable(request.Query, tokens))
        {
            return new PagedResult<NovelSearchResult>([], 0, pageSize, pageNumber);
        }

        var phrase = string.Join(' ', tokens);
        var wordStart = " " + phrase;

        var query = dbContext.Novels.AsNoTracking()
            .Where(n => !n.IsDraft);

        foreach (var token in tokens)
        {
            query = query.Where(n => n.SearchTitle.Contains(token));
        }

        if (request.Genres is { Count: > 0 })
        {
            var genres = request.Genres;
            query = query.Where(n => n.NovelGenres.Any(ng => genres.Contains(ng.Genre.Name) || genres.Contains(ng.Genre.Slug)));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status;
            query = query.Where(n => n.Status == status);
        }

        var ranges = new HashSet<ChapterCountRange>(request.ChapterRanges ?? []);
        if (request.ChapterRange.HasValue)
        {
            ranges.Add(request.ChapterRange.Value);
        }

        if (ranges.Count > 0)
        {
            var upTo10 = ranges.Contains(ChapterCountRange.Range_1_10);
            var upTo20 = ranges.Contains(ChapterCountRange.Range_10_20);
            var upTo50 = ranges.Contains(ChapterCountRange.Range_20_50);
            var over50 = ranges.Contains(ChapterCountRange.Range_50_Plus);

            query = query
                .Select(n => new { Novel = n, Chapters = n.Chapters.Count(c => c.Status == Published) })
                .Where(x => (upTo10 && x.Chapters >= 1 && x.Chapters <= 10)
                         || (upTo20 && x.Chapters > 10 && x.Chapters <= 20)
                         || (upTo50 && x.Chapters > 20 && x.Chapters <= 50)
                         || (over50 && x.Chapters > 50))
                .Select(x => x.Novel);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var ordered = request.SortBy switch
        {
            NovelSortBy.Newest => query.OrderByDescending(n => n.CreatedAt),
            NovelSortBy.LastUpdated => query.OrderByDescending(n => n.LastUpdatedAt),
            NovelSortBy.HighestRated => query.OrderByDescending(n => n.TotalAverageScore).ThenByDescending(n => n.ReviewCount),
            NovelSortBy.MostReviewed => query.OrderByDescending(n => n.ReviewCount).ThenByDescending(n => n.TotalAverageScore),
            NovelSortBy.MostPopular => ByPopularity(query),
            _ when tokens.Count == 0 => ByPopularity(query),
            _ => query
                .OrderBy(n => n.SearchTitle == phrase ? 0
                    : n.SearchTitle.StartsWith(phrase) ? 1
                    : n.SearchTitle.Contains(wordStart) ? 2
                    : n.SearchTitle.Contains(phrase) ? 3
                    : 4)
                .ThenByDescending(n => dbContext.UserNovelProgress.Count(p => p.NovelId == n.Id))
                .ThenByDescending(n => n.TotalViews)
        };

        var items = await ordered
            .ThenBy(n => n.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NovelSearchResult
            {
                Id = n.Id,
                Title = n.Title,
                Slug = n.Slug,
                Summary = n.Summary,
                CoverImageUrl = n.CoverImageUrl,
                Status = n.Status,
                Genres = n.NovelGenres.Select(ng => ng.Genre.Name).ToList(),
                ChapterCount = n.Chapters.Count(c => c.Status == Published),
                TotalAverageScore = n.TotalAverageScore,
                ReviewCount = n.ReviewCount,
                TotalViews = n.TotalViews,
                CreatedAt = n.CreatedAt,
                LastUpdatedAt = n.LastUpdatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<NovelSearchResult>(items, totalCount, pageSize, pageNumber);
    }

    // Readers (people with reading progress) first: TotalViews includes years of undeduplicated hits.
    private IOrderedQueryable<Domain.Entities.Novel> ByPopularity(IQueryable<Domain.Entities.Novel> query) =>
        query
            .OrderByDescending(n => dbContext.UserNovelProgress.Count(p => p.NovelId == n.Id))
            .ThenByDescending(n => n.TotalViews);
}
