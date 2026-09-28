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
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.WorkGuid) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        logger.LogInformation("Getting work {NovelId}", novel.Id);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        }
        var result = mapper.Map<WorkDTO>(novel);
        return result;
    }
}
