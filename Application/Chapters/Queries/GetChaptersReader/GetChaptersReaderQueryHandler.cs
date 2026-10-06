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
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var chapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var chapterDtos = mapper.Map<IEnumerable<ChaptersDTO>>(chapters).ToList();
        
        // Each chapter's own early-access lock (#94), for this reader: never for the novel's author or a subscriber.
        var view = await privilegeService.GetViewAsync(novel.Id, novel.AuthorId, userContext.GetCurrentUser()?.Id);
        var byId = chapters.ToDictionary(c => c.Id);
        foreach (var dto in chapterDtos)
        {
            var chapter = byId[dto.Id];
            dto.IsLocked = view.IsLockedForViewer(chapter);
            dto.UnlocksAt = view.UnlocksAtForViewer(chapter);
            dto.IsEarlyAccess = view.IsLocked(chapter);
        }
        
        return chapterDtos;
    }
}
