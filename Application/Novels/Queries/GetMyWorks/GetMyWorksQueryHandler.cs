using Amazon.Runtime;
using Application.Common;
using Application.Novels.DTOS;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetMyWorks;

public class GetMyWorksQueryHandler(ILogger<GetMyWorksQueryHandler> logger, IUserContext userContext, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<GetMyWorksQuery, PagedResult<MyWorksDTO>>
{
    public async Task<PagedResult<MyWorksDTO>> Handle(GetMyWorksQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        logger.LogInformation("Getting all works for user {@user}", currentUser);
        var (userNovels, totalCount) = await novelsRepository.GetWorks(currentUser.Id, request.PageNumber, request.PageSize);
        var userWorksList = mapper.Map<IEnumerable<MyWorksDTO>>(userNovels);
        var result = new PagedResult<MyWorksDTO>(userWorksList, totalCount, request.PageSize, request.PageNumber);
        return result;

    }
}
