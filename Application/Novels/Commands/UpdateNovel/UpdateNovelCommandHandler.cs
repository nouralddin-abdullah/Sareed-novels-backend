using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.UpdateNovel;

public class UpdateNovelCommandHandler(
    ILogger<UpdateNovelCommandHandler> logger, 
    IUserContext userContext, 
    INovelGenresRepository novelGenresRepository,
    INovelsRepository novelsRepository, 
    IMapper mapper,
    INovelRecommendationService recommendationService) : IRequestHandler<UpdateNovelCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateNovelCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        logger.LogInformation("Updating data for novel {NovelId}", novel.Id);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }
        if (request.Title != null)
        {
            // The edit form resends the unchanged title on every save; only a real rename may move the novel's URL.
            var newSlug = Slugs.For(novel.Id, request.Title);
            if (newSlug != Slugs.For(novel.Id, novel.Title))
            {
                novel.Slug = newSlug;
            }
        }
        mapper.Map(request, novel);
        bool novelUpdateResult = await novelsRepository.UpdateOne(novel);
        if (!novelUpdateResult)
        {
            return new OperationResult
            {
                Message = "Novel wasn't updated successfully",
                Success = false
            };
        }
        if (request.GenreIds != null)
        {
            //Validation

            if (request.GenreIds.Count == 0 || request.GenreIds.Count > 4)
            {
                return new OperationResult
                {
                    Message = "A novel must have between 1 and 4 genres",
                    Success = false
                };
            }
            bool genresUpdateResult = await novelGenresRepository.UpdateNovelGenres(request.NovelId, request.GenreIds);
            if (!genresUpdateResult)
            {
                return new OperationResult
                {
                    Message = "Novel was updated but failed to update genres. Please check that all selected genres exist.",
                    Success = false
                };
            }
        }

        // Invalidate recommendation cache (summary or genres may have changed)
        await recommendationService.InvalidateRecommendationCacheAsync(request.NovelId);

        return new OperationResult
        {
            Message = "Novel was updated successfully",
            Success = true
        };
    }
}
