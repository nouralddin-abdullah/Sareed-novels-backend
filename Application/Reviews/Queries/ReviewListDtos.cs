using Application.Reviews.DTO;
using Application.Users;
using AutoMapper;
using Domain.Entities;
using Domain.Repositories;

namespace Application.Reviews.Queries;

/// <summary>
/// Reviews as a novel's review list returns them: mapped, with whether the signed-in reader liked each. Creating a
/// review returns the new one through here too, so the two can't differ.
/// </summary>
internal static class ReviewListDtos
{
    public static async Task<List<ReviewsDTO>> Build(
        IEnumerable<Review> reviews,
        IMapper mapper,
        IReviewLikesRepository reviewLikesRepository,
        CurrentUser? currentUser)
    {
        var reviewDtos = mapper.Map<List<ReviewsDTO>>(reviews);

        if (currentUser != null && reviewDtos.Count != 0)
        {
            var likedReviewIds = await reviewLikesRepository.GetUserLikedReviewIds(currentUser.Id, reviewDtos.Select(r => r.Id));
            foreach (var reviewDto in reviewDtos)
            {
                reviewDto.IsLikedByCurrentUser = likedReviewIds.Contains(reviewDto.Id);
            }
        }

        return reviewDtos;
    }
}
