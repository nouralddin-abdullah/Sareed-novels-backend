using Application.Novels.DTOS;
using Application.Novels.Queries.GetMyWorks;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetWork;

public class GetWorkQueryHandler(ILogger<GetMyWorksQueryHandler> logger, IUserContext userContext, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<GetWorkQuery, WorkDTO>
{
    public async Task<WorkDTO> Handle(GetWorkQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.WorkGuid) ?? throw new NotFoundException("No work was found with this id", "NovelNotFound");
        logger.LogInformation("Getting work {NovelId}", novel.Id);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden", "NotOwner");
        }
        var result = mapper.Map<WorkDTO>(novel);
        return result;
    }
}
