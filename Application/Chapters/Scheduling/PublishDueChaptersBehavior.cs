using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Scheduling;

/// <summary>
/// A request that reads one novel or its chapters: the novel page (by slug or id), its chapter list and a chapter, and
/// the author's work, chapter list and chapter. Before it is handled, the novel's chapters whose scheduled time has come
/// are published (<see cref="PublishDueChaptersBehavior{TRequest,TResponse}"/>, #77).
/// </summary>
public interface IReadsNovel
{
    /// <summary>The novel, by id; null when the request names it by slug.</summary>
    Guid? ReadNovelId { get; }

    /// <summary>The novel, by slug, when the request names it so.</summary>
    string? ReadNovelSlug => null;
}

/// <summary>
/// Publishes a novel's due chapters (<see cref="ScheduledChapterPublisher"/>) before a request that reads it or its
/// chapters (<see cref="IReadsNovel"/>) is handled, so the answer has them. The scheduler publishes due chapters every
/// minute while the app runs, but the host may stop the app while it is idle: then the first request reading the novel
/// finds them due, and they come out before it is answered, as they would have on time. It runs in a scope of its own,
/// so what the request then reads is loaded after the publish. If it fails, the request is answered anyway (logged as
/// an error), and the scheduler publishes them on its next run.
/// </summary>
public sealed class PublishDueChaptersBehavior<TRequest, TResponse>(
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledChapterPublisher> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IReadsNovel read && (read.ReadNovelId is not null || read.ReadNovelSlug is not null))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                // Not the request's token: a chapter stored published gets everything a publish does.
                await scope.ServiceProvider.GetRequiredService<ScheduledChapterPublisher>()
                    .PublishDueAsync(read.ReadNovelId, read.ReadNovelSlug, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Publishing the due chapters of novel {Novel} before reading it failed; the scheduler will retry",
                    read.ReadNovelId?.ToString() ?? read.ReadNovelSlug);
            }
        }

        return await next();
    }
}
