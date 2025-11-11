using Application.Entities.DTOs;
using Application.Users;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Entities.Queries.GetSections;

public class GetSectionsQueryHandler(
    ILogger<GetSectionsQueryHandler> logger,
    INovelEntityRepository entityRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext) : IRequestHandler<GetSectionsQuery, SectionsResponseDTO>
{
    public async Task<SectionsResponseDTO> Handle(GetSectionsQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting sections for novel {NovelId}", request.NovelId);

        // Check ownership
        var novel = await novelsRepository.GetOne(request.NovelId);
        var currentUser = userContext.GetCurrentUser();
        var isOwner = currentUser != null && novel != null && novel.AuthorId == currentUser.Id;

        // Get sections with icons in one query (includes placeholders)
        var sectionsWithIcons = await entityRepository.GetSectionsWithIconsAsync(request.NovelId);

        // Build section summaries
        var sections = sectionsWithIcons
            .Select(s => new SectionSummaryDTO
            {
                Name = s.Section,
                Icon = s.Icon
            })
            .ToList();

        return new SectionsResponseDTO
        {
            Sections = sections,
            IsOwner = isOwner
        };
    }
}
