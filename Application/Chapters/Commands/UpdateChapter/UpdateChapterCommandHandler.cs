using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommandHandler(ILogger<UpdateChapterCommandHandler> logger, IChaptersRepository chaptersRepository, INovelsRepository novelsRepository, IUserContext userContext, IMapper mapper) : IRequestHandler<UpdateChapterCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Updateing chapter {@chapter}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new NotFoundException("Chapter wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        if (request.Title != null)
        {
            chapter.Slug = $"{chapter.Id.ToString().Substring(0, 5)}-{request.Title.Replace(" ", "-").ToLower()}";
        }
        //updating 
        mapper.Map(request, chapter);
        var result = await chaptersRepository.UpdateChapter(chapter);
        if (result)
        {
            return new OperationResult
            {
                Success = true,
                Message = "Update chapter is successful"
            };
        }
        return new OperationResult
        {
            Success = false,
            Message = "Update chapter is not successful"
        };
    }
}
