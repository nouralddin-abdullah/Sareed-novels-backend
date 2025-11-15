using Application.Notifications.DTOs;
using AutoMapper;
using Domain.Entities;

namespace Application.Notifications.DTOs;

public class NotificationProfile : Profile
{
    public NotificationProfile()
    {
        CreateMap<Notification, NotificationDto>();
        
        CreateMap<Domain.Entities.Comments, CommentDto>()
            .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
        
        CreateMap<Domain.Entities.Comments, CommentReplyDto>()
            .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
        
        CreateMap<User, CommentUserDto>();
    }
}
