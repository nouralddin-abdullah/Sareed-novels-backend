using AutoMapper;
using Domain.Entities;

namespace Application.Posts.DTOs;

public class PostProfile : Profile
{
    public PostProfile()
    {
        CreateMap<User, PostUserDTO>();
        
        CreateMap<Novel, PostNovelDTO>();

        CreateMap<Post, PostDTO>()
            .ForMember(dest => dest.IsLikedByCurrentUser, opt => opt.Ignore());
    }
}
