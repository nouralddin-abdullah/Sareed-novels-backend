using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Services;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChaptersReader;

public class GetChaptersReaderQueryHandler(
    ILogger<GetChaptersAuthorQueryHandler> logger, 
    IChaptersRepository chaptersRepository, 
    INovelsRepository novelsRepository, 
    IMapper mapper,
    IUserContext userContext,
    IPrivilegeService privilegeService) : IRequestHandler<GetChaptersReaderQuery, IEnumerable<ChaptersDTO>>
{
    public async Task<IEnumerable<ChaptersDTO>> Handle(GetChaptersReaderQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting reader view chapters for {@novel}", request);
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var chapterDtos = mapper.Map<IEnumerable<ChaptersDTO>>(chapters).ToList();
        
        var currentUser = userContext.GetCurrentUser();
        
        // ✅ Authors can see all their own chapters unlocked (skip privilege check)
        if (currentUser != null && novel.AuthorId == currentUser.Id)
        {
            return chapterDtos; // All chapters unlocked for author
        }
        
        // ✅ OPTIMIZED: Get privilege config once (no chapter loading)
        var privilege = await privilegeService.GetPrivilegeConfigAsync(request.NovelId);
        
        // Check if user has subscription
        var hasSubscription = false;
        if (currentUser != null && privilege != null && privilege.IsEnabled)
        {
            hasSubscription = await privilegeService.HasActiveSubscriptionAsync(request.NovelId, currentUser.Id);
        }
        
        // ✅ Mark locked chapters using in-memory sequence comparison (no extra queries!)
        if (!hasSubscription && privilege != null && privilege.IsEnabled && privilege.PrivilegeStartSequence.HasValue)
        {
            foreach (var dto in chapterDtos)
            {
                var chapter = chapters.First(c => c.Id == dto.Id);
                if (chapter.PublishedChapterSequence.HasValue && 
                    chapter.PublishedChapterSequence.Value >= privilege.PrivilegeStartSequence.Value)
                {
                    dto.IsLocked = true;
                }
            }
        }
        
        return chapterDtos;
    }
}
