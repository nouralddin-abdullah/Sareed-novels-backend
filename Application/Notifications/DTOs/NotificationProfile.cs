using Application.Notifications.DTOs;
using AutoMapper;
using Domain.Entities;

namespace Application.Notifications.DTOs;

public class NotificationProfile : Profile
{
    public NotificationProfile()
    {
        CreateMap<Notification, NotificationDto>()
            // Set by GetNotificationsQueryHandler (GiftId, GiftCount and GiftTransactionId are the row's own).
            .ForMember(dest => dest.NovelId, opt => opt.Ignore())
            .ForMember(dest => dest.NovelSlug, opt => opt.Ignore())
            .ForMember(dest => dest.NovelTitle, opt => opt.Ignore())
            .ForMember(dest => dest.ChapterId, opt => opt.Ignore())
            .ForMember(dest => dest.ChapterTitle, opt => opt.Ignore())
            .ForMember(dest => dest.ReadingListName, opt => opt.Ignore())
            .ForMember(dest => dest.GiftNameAr, opt => opt.Ignore())
            .ForMember(dest => dest.GiftMessage, opt => opt.Ignore());
        
        CreateMap<Domain.Entities.Comments, CommentDto>()
            .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
        
        CreateMap<Domain.Entities.Comments, CommentReplyDto>()
            .ForMember(dest => dest.User, opt => opt.MapFrom(src => src.User));
        
        CreateMap<User, CommentUserDto>();
    }
}
