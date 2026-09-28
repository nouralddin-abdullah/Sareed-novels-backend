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
    IGenresRepository genresRepository,
    INovelsRepository novelsRepository, 
    IMapper mapper,
    INovelRecommendationService recommendationService) : IRequestHandler<UpdateNovelCommand, OperationResult>
{
    public async Task<OperationResult> Handle(UpdateNovelCommand request, CancellationToken cancellationToken)
    {
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        logger.LogInformation("Updating data for novel {NovelId}", novel.Id);
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
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
                    Code = "InvalidGenres",
                    Message = "اختر من 1 إلى 4 تصنيفات مختلفة من التصنيفات المتاحة",
                    Success = false
                };
            }
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
                Code = "OperationFailed",
                Message = "تعذّر تحديث الرواية. حاول مرة أخرى.",
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
                    Code = "InvalidGenres",
                    Message = "حُدّثت الرواية، لكن تعذّر تحديث تصنيفاتها. تأكد من أن التصنيفات المختارة متاحة.",
                    Success = false
                };
            }
        }

        // Invalidate recommendation cache (summary or genres may have changed)
        await recommendationService.InvalidateRecommendationCacheAsync(request.NovelId);

        return new OperationResult
        {
            Message = "تم تحديث الرواية",
            Success = true
        };
    }
}
