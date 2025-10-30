using Application.Chapters.DTOS;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterCommandHandler(ILogger<CreateChapterCommandHandler> logger, IChaptersRepository chaptersRepository, IUserContext userContext, INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<CreateChapterCommand, ChapterSingleAuthorDTO>
{
    public async Task<ChapterSingleAuthorDTO> Handle(CreateChapterCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Adding new chapter for novel {NovelId}", request.NovelId);
        
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel wasn't found");
        
        if (novel.AuthorId != currentUser.Id) 
            throw new ForbidException("User doesn't own this novel");
        
        var chapter = mapper.Map<Chapter>(request);
        chapter.ChapterIndex = await chaptersRepository.GetNextChapterIndex(novel.Id);
        chapter.Id = Guid.NewGuid();
        chapter.Slug = $"{chapter.Id.ToString()[..5]}-{request.Title.Replace(" ", "-").ToLower()}";
        
        var result = await chaptersRepository.CreateChapter(chapter);
        if (!result)
        {
            throw new InvalidOperationException("Failed to create the chapter");
        }

        // Update denormalized chapter count
        novel.ChapterCount++;
        novel.LastUpdatedAt = DateTime.UtcNow;
        await novelsRepository.UpdateOne(novel);
        
        // Map the created chapter to DTO and return it
        var chapterDto = mapper.Map<ChapterSingleAuthorDTO>(chapter);
        
        logger.LogInformation("Chapter {ChapterId} created successfully for novel {NovelId}. Chapter count: {ChapterCount}", 
            chapter.Id, novel.Id, novel.ChapterCount);
        
        return chapterDto;
    }
}
