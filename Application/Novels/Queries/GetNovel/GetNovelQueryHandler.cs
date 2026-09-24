using Application.Novels.DTOS;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetNovel;

public class GetNovelQueryHandler(
    ILogger<GetNovelQueryHandler> logger,
    INovelsRepository novelsRepository,
    IMapper mapper,
    IUserContext userContext,
    IVisitorContext visitorContext,
    IServiceScopeFactory scopeFactory) : IRequestHandler<GetNovelQuery, NovelsDTO>
{
    public async Task<NovelsDTO> Handle(GetNovelQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting novel slug {slug}", request.NovelSlug);
        var novel = await novelsRepository.GetOneBySlug(request.NovelSlug) ?? throw new NotFoundException("This novel was not found");
        var novelDto = mapper.Map<NovelsDTO>(novel);

        // Resolve the visitor now: the background task outlives the request and can't read HttpContext.
        var visitorKey = visitorContext.GetVisitorKey();
        var isAuthor = userContext.GetCurrentUser()?.Id == novel.AuthorId;
        if (visitorKey != null && !isAuthor)
        {
            _ = TrackViewInBackground(novel.Id, visitorKey);
        }

        return novelDto;
    }

    private async Task TrackViewInBackground(Guid novelId, string visitorKey)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var viewTracking = scope.ServiceProvider.GetRequiredService<IViewTrackingService>();
            await viewTracking.TrackNovelView(novelId, visitorKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to track view for novel {NovelId} in background", novelId);
        }
    }
}
