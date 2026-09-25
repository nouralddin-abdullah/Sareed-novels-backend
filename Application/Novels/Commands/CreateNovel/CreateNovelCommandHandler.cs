using Application.Services;
using Application.Users;
using Application.Users.Commands.FollowUser;
using AutoMapper;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using Domain.Seo;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Novels.Commands.CreateNovel;

public class CreateNovelCommandHandler(
    ILogger<CreateNovelCommandHandler> logger, 
    IMapper mapper, 
    IUserContext userContext,
    IGenresRepository genresRepository, 
    INovelsRepository novelsRepository, 
    IFileUploadService fileUploadService) : IRequestHandler<CreateNovelCommand, CreateNovelResult>
{
    public async Task<CreateNovelResult> Handle(CreateNovelCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Creating new novel {Title}", request.Title);
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("User not signed in");

        // Check the genres before anything is uploaded or saved, so a bad genre id can't leave a genre-less novel.
        var knownGenreIds = (await genresRepository.GetAllGenres()).Select(g => g.Id).ToHashSet();
        if (!request.GenreIds.All(knownGenreIds.Contains))
        {
            return new CreateNovelResult
            {
                Success = false,
                Message = "Unknown genre"
            };
        }

        var novel = mapper.Map<Novel>(request);
        novel.Id = Guid.NewGuid();
        novel.Status = NovelStatus.Ongoing.ToString();
        novel.Slug = Slugs.For(novel.Id, request.Title);
        novel.CreatedAt = DateTime.UtcNow;
        novel.LastUpdatedAt = DateTime.UtcNow;
        novel.TotalViews = 0;
        novel.AuthorId = currentUser.Id;
        novel.RecalculateAverageScores();
        if (request.CoverImageUrl != null)
        {
            using var stream = request.CoverImageUrl.OpenReadStream();
            novel.CoverImageUrl = await fileUploadService.UploadNovelImageAsync(
                stream,
                request.CoverImageUrl.ContentType,
                novel.Id.ToString()
                );
        }
        // The novel and its genres are inserted together (one SaveChanges), never one without the other.
        novel.NovelGenres = request.GenreIds
            .Distinct()
            .Select(genreId => new NovelGenre { NovelId = novel.Id, GenreId = genreId, AddedAt = novel.CreatedAt })
            .ToList();

        var novelResult = await novelsRepository.CreateNovel(novel);
        if (!novelResult)
        {
            return new CreateNovelResult
            {
                Message = "Error while creating novel",
                Success = false
            };
        }


        return new CreateNovelResult
        {
            Message = "Novel was created successfully",
            Success = true,
            NovelId = novel.Id
        };
    }
}
