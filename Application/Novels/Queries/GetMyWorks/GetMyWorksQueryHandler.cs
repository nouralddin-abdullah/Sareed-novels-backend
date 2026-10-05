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
    /// <summary>The web's new-post dialog lists up to 100 of the author's works.</summary>
    public const int MaxPageSize = 100;

    public async Task<PagedResult<MyWorksDTO>> Handle(GetMyWorksQuery request, CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Getting all works for user {UserId}", currentUser.Id);
        // A size of 0 or less used to return every work.
        var (pageNumber, pageSize) = Paging.Clamp(request.PageNumber, request.PageSize, MaxPageSize);
        var (userNovels, totalCount) = await novelsRepository.GetWorks(currentUser.Id, pageNumber, pageSize);
        var userWorksList = mapper.Map<List<MyWorksDTO>>(userNovels);
        // Each novel's words, drafts included (#77), in one query for the page.
        var words = await novelsRepository.GetWordsCountsAsync(userWorksList.Select(w => w.Id).ToList());
        userWorksList.ForEach(work => work.WordsCount = words[work.Id]);
        var result = new PagedResult<MyWorksDTO>(userWorksList, totalCount, pageSize, pageNumber);
        return result;

    }
}
