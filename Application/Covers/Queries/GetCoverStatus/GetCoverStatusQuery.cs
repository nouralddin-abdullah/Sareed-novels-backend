using Application.Services;
using Domain.Repositories;
using MediatR;

namespace Application.Covers.Queries.GetCoverStatus;

/// <summary>How many covers are in the standard format, and whether this host can process images.</summary>
public class GetCoverStatusQuery : IRequest<CoverStatus>;

public record CoverStatus(bool ProcessorAvailable, int TotalNovels, int StandardCovers, int LegacyCovers);

public class GetCoverStatusQueryHandler(INovelsRepository novelsRepository, INovelCoverService coverService)
    : IRequestHandler<GetCoverStatusQuery, CoverStatus>
{
    public async Task<CoverStatus> Handle(GetCoverStatusQuery request, CancellationToken cancellationToken)
    {
        var (total, legacy) = await novelsRepository.CountCoversAsync(NovelCovers.StandardUrlMarker, cancellationToken);
        return new CoverStatus(coverService.ProcessorAvailable, total, total - legacy, legacy);
    }
}
