using Application.Users.Commands.CreateUser;
using Application.Users.Commands.UpdateMe;
using AutoMapper;
using Domain.Entities;

namespace Application.Users.DTOS;

public class UserDTOS : Profile
{
    public UserDTOS()
    {
        //Create User Command
        CreateMap<CreateUserCommand, User>();

        //Get User Query
        // reviewsCount and commentsCount are the totals of the member's lists, counted by the profile handlers (#54),
        // not the stored User.ReviewsCount and User.CommentsCount.
        CreateMap<User, UserIsProfile>()
            .ForMember(dest => dest.TotalFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewsCount, opt => opt.Ignore())
            .ForMember(dest => dest.CommentsCount, opt => opt.Ignore())
            .ForMember(dest => dest.HasPassword, opt => opt.MapFrom(src => src.PasswordHash != null));

        CreateMap<User, UserProfile>()
            .ForMember(dest => dest.TotalFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.ReviewsCount, opt => opt.Ignore())
            .ForMember(dest => dest.CommentsCount, opt => opt.Ignore())
            .ForMember(dest => dest.IsFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.IsBlockedByMe, opt => opt.Ignore());

        CreateMap<Follow, FollowerDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.Follower.Id))
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Follower.UserName))
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Follower.DisplayName))
            .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.Follower.ProfilePhoto))
            .ForMember(dest => dest.IsFollowing, opt => opt.Ignore());

        CreateMap<Follow, FollowedDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.Followed.Id))
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Followed.UserName))
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Followed.DisplayName))
            .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.Followed.ProfilePhoto))
            .ForMember(dest => dest.IsFollowing, opt => opt.Ignore());

        CreateMap<UpdateMeCommand, User>()
            .ForMember(dest => dest.ProfilePhoto, opt => opt.Ignore())
            .ForMember(dest => dest.ProfileBanner, opt => opt.Ignore())
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
    }
}
