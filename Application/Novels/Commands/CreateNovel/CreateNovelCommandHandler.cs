using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.CreateNovel;

public class CreateNovelCommandHandler(ILogger<CreateNovelCommandHandler> logger, IMapper mapper, IUserContext userContext, INovelsRepository novelsRepository, IFileUploadService fileUploadService) : IRequestHandler<CreateNovelCommand, OperationResult>
{
    public async Task<OperationResult> Handle(CreateNovelCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new novel {@novel}", request);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");
        var novel = mapper.Map<Novel>(request);
        novel.Status = NovelStatus.Ongoing.ToString();
        novel.CreatedAt = DateTime.UtcNow;
        novel.LastUpdatedAt = DateTime.UtcNow;
        novel.TotalViews = 0;
        novel.AuthorId = currentUser.Id;
        if (request.CoverImageUrl != null)
        {
            using var stream = request.CoverImageUrl.OpenReadStream();
            novel.CoverImageUrl = await fileUploadService.UploadNovelImageAsync(
                stream,
                request.CoverImageUrl.ContentType,
                request.Title
                );
        }
        var result = await novelsRepository.CreateNovel(novel);
        if (result)
        {
            return new OperationResult
            {
                Message = "Novel was created successfully",
                Success = true
            };
        }
        else
        {
            return new OperationResult
            {
                Message = "Error while creating novel",
                Success = false
            };
        }

    }
}
