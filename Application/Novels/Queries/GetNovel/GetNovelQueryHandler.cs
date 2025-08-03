using Application.Novels.DTOS;
using Application.Novels.Queries.GetMyWorks;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Queries.GetNovel;

public class GetNovelQueryHandler(ILogger<GetMyWorksQueryHandler> logger, IUserContext userContext, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<GetNovelQuery, NovelsDTO>
{
    public async Task<NovelsDTO> Handle(GetNovelQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting novel slug {slug}", request.NovelSlug);
        var novel = await novelsRepository.GetOneBySlug(request.NovelSlug) ?? throw new NotFoundException("This novel was not found");
        var novelDto = mapper.Map<NovelsDTO>(novel);
        return novelDto;
    }
}
