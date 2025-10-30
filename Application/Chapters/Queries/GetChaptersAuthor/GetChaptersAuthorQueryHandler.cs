using Application.Chapters.Commands.UpdateChapter;
using Application.Chapters.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChaptersAuthor;

public class GetChaptersAuthorQueryHandler(ILogger<GetChaptersAuthorQueryHandler> logger, IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetChaptersAuthorQuery, IEnumerable<ChaptersDTO>>
{
    public async Task<IEnumerable<ChaptersDTO>> Handle(GetChaptersAuthorQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting author view chapters for {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        var chapters = await chaptersRepository.GetChaptersAuthorView(request.NovelId);
        var result = mapper.Map<IEnumerable<ChaptersDTO>>(chapters);
        return result;
    }
}
