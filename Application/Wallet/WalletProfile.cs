using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Entities;

namespace Application.Wallet;

public class WalletProfile : Profile
{
    public WalletProfile()
    {
        CreateMap<UserWallet, WalletDto>();
        
        CreateMap<RechargeRequest, RechargeRequestDto>()
            .ForMember(dest => dest.UserDisplayName, opt => opt.MapFrom(src => src.User != null ? src.User.DisplayName : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null));
        
        CreateMap<WithdrawalRequest, WithdrawalRequestDto>()
            .ForMember(dest => dest.UserDisplayName, opt => opt.MapFrom(src => src.User != null ? src.User.DisplayName : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null));
        
        CreateMap<PointTransaction, PointTransactionDto>();
    }
}
