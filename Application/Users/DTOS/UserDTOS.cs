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
        CreateMap<User, UserIsProfile>()
            .ForMember(dest => dest.RecentFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.RecentFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowing, opt => opt.Ignore());

        CreateMap<User, UserProfile>()
            .ForMember(dest => dest.RecentFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.RecentFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowers, opt => opt.Ignore())
            .ForMember(dest => dest.TotalFollowing, opt => opt.Ignore())
            .ForMember(dest => dest.IsFollowing, opt => opt.Ignore());

        CreateMap<Follow, FollowerDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.Follower.Id))
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Follower.UserName))
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Follower.DisplayName))
            .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.Follower.ProfilePhoto));

        CreateMap<Follow, FollowedDto>()
            .ForMember(dest => dest.UserId, opt => opt.MapFrom(src => src.Followed.Id))
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.Followed.UserName))
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.Followed.DisplayName))
            .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.Followed.ProfilePhoto));

        CreateMap<UpdateMeCommand, User>()
            .ForMember(dest => dest.ProfilePhoto, opt => opt.Ignore())
            .ForMember(dest => dest.ProfileBanner, opt => opt.Ignore())
            .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
    }
}
