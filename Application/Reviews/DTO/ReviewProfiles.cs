using Application.Novels.DTOS;
using Application.Reviews.Commands.CreateReview;
using AutoMapper;
using Domain.Entities;

namespace Application.Reviews.DTO;

public class ReviewProfiles : Profile
{
    public ReviewProfiles()
    {
        CreateMap<CreateReviewCommand, Review>();

        CreateMap<User, ReviewerDTO>();

        CreateMap<Review, ReviewsDTO>()
            .ForMember(dest => dest.Reviewer, opt => opt.MapFrom(src => src.ReviewOwner));

        CreateMap<Review, CurrentUserReviewDTO>();
    }
}
