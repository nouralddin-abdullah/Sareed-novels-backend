using Application.Chapters.DTOS;
using Application.Chapters.Queries.GetChaptersAuthor;
using Application.Users;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Queries.GetChaptersReader;

public class GetChaptersReaderQueryHandler(ILogger<GetChaptersAuthorQueryHandler> logger, IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<GetChaptersReaderQuery, IEnumerable<ChaptersDTO>>
{
    public async Task<IEnumerable<ChaptersDTO>> Handle(GetChaptersReaderQuery request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting reader view chapters for {@novel}", request);
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapters = await chaptersRepository.GetChaptersReaderView(request.NovelId);
        var result = mapper.Map<IEnumerable<ChaptersDTO>>(chapters);
        return result;
    }
}
