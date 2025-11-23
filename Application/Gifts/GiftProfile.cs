using Application.Gifts.DTOs;
using AutoMapper;
using Domain.Entities;

namespace Application.Gifts;

public class GiftProfile : Profile
{
    public GiftProfile()
    {
        CreateMap<Gift, GiftDto>();
        
        CreateMap<GiftTransaction, GiftTransactionDto>()
            .ForMember(dest => dest.SenderUserName, opt => opt.MapFrom(src => src.Sender.UserName))
            .ForMember(dest => dest.SenderDisplayName, opt => opt.MapFrom(src => src.Sender.DisplayName))
            .ForMember(dest => dest.SenderProfilePhoto, opt => opt.MapFrom(src => src.Sender.ProfilePhoto));
        
        CreateMap<GlobalSupporterLeaderboard, TopSupporterDto>()
            .ForMember(dest => dest.UserName, opt => opt.MapFrom(src => src.User.UserName))
            .ForMember(dest => dest.DisplayName, opt => opt.MapFrom(src => src.User.DisplayName))
            .ForMember(dest => dest.ProfilePhoto, opt => opt.MapFrom(src => src.User.ProfilePhoto));
    }
}
