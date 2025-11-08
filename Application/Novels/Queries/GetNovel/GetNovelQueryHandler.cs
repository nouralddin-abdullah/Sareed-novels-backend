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
    IServiceProvider serviceProvider) : IRequestHandler<GetNovelQuery, NovelsDTO>
{
    public async Task<NovelsDTO> Handle(GetNovelQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting novel slug {slug}", request.NovelSlug);
        var novel = await novelsRepository.GetOneBySlug(request.NovelSlug) ?? throw new NotFoundException("This novel was not found");
        var novelDto = mapper.Map<NovelsDTO>(novel);

        // Fire-and-forget background tracking with proper scope
        _ = TrackViewInBackground(novelDto.Id);

        return novelDto; // Returns immediately without waiting for view tracking
    }

    private async Task TrackViewInBackground(Guid novelId)
    {
        try
        {
            // Create a new scope for background operation (this fixes the disposed context issue)
            using var scope = serviceProvider.CreateScope();
            var backgroundViewTrackingService = scope.ServiceProvider.GetRequiredService<IViewTrackingService>();

            // This runs in the background with a fresh ApplicationDbContext
            await backgroundViewTrackingService.TrackNovelView(novelId);

            logger.LogDebug("Successfully tracked view for novel {NovelId}", novelId);
        }
        catch (Exception ex)
        {
            // Log error but don't propagate (fire-and-forget)
            logger.LogWarning(ex, "Failed to track view for novel {NovelId} in background", novelId);
        }
    }
}