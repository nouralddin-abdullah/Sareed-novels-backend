using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.DeleteChapter;

public class DeleteChapterCommandHandler(ILogger<DeleteChapterCommandHandler> logger, INovelsRepository novelsRepository, IChaptersRepository chaptersRepository, IUserContext userContext) : IRequestHandler<DeleteChapterCommand, bool>
{
    public async Task<bool> Handle(DeleteChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting chapter {@chapter}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        var chapter = await chaptersRepository.GetChapterById(request.ChapterId) ?? throw new  NotFoundException("Chapter wasn't found");
        if (novel.AuthorId != currentUser.Id) throw new ForbidException("User doesn't own this novel");
        var deleteResult = await chaptersRepository.DeleteChapter(chapter);
        if (deleteResult)
        {
            novel.ChapterCount--;
            await novelsRepository.UpdateOne(novel);
        }
        return deleteResult;
    }
}
