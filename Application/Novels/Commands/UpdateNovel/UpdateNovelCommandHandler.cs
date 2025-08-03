using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.UpdateNovel;

public class UpdateNovelCommandHandler(ILogger<UpdateNovelCommandHandler> logger, IUserContext userContext, INovelGenresRepository novelGenresRepository,INovelsRepository novelsRepository, IMapper mapper) : IRequestHandler<UpdateNovelCommand, OperationResult>
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
        return new OperationResult
        {
            Message = "Novel was updated successfully",
            Success = true
        };
    }
}
