using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.UpdateNovel;

public class UpdateNovelCommandHandler(
    ILogger<UpdateNovelCommandHandler> logger, 
    IUserContext userContext, 
    INovelGenresRepository novelGenresRepository,
    IGenresRepository genresRepository,
    INovelsRepository novelsRepository, 
    IMapper mapper,
    INovelRecommendationService recommendationService) : IRequestHandler<UpdateNovelCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateNovelCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("This novel was not found");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("This user not signed in");
        logger.LogInformation("Updating data for novel {@novel}", novel);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("Forbidden");
        }

        // Validate the genres before changing anything, so a bad genre list can't leave a half-applied update.
        if (request.GenreIds != null)
        {
            var knownGenreIds = (await genresRepository.GetAllGenres()).Select(g => g.Id).ToHashSet();
            if (request.GenreIds.Count == 0 || request.GenreIds.Count > 4
                || request.GenreIds.Distinct().Count() != request.GenreIds.Count
                || !request.GenreIds.All(knownGenreIds.Contains))
            {
                return new OperationResult
                {
                    Message = "A novel must have between 1 and 4 different, existing genres",
                    Success = false
                };
            }
        }

        if (request.Title != null)
        {
            novel.Slug = $"{novel.Id.ToString().Substring(0, 5)}-{request.Title.Replace(" ", "-").ToLower()}";
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
