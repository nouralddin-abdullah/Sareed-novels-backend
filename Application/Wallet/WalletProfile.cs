using Application.Wallet.DTOs;
using AutoMapper;
using Domain.Entities;

namespace Application.Wallet;

public class WalletProfile : Profile
{
    public WalletProfile()
    {
        // What is withdrawable and what was earned come from the wallet service (GetMyWalletQueryHandler). The wallet's
        // TotalEarned column was never written (always 0); the ledger says what was earned (#78).
        CreateMap<UserWallet, WalletDto>()
            .ForMember(dest => dest.TotalEarned, opt => opt.Ignore())
            .ForMember(dest => dest.Withdrawable, opt => opt.Ignore())
            .ForMember(dest => dest.PendingEarnings, opt => opt.Ignore())
            .ForMember(dest => dest.NextReleaseAt, opt => opt.Ignore());

        CreateMap<RechargeRequest, RechargeRequestDto>()
            .ForMember(dest => dest.UserDisplayName, opt => opt.MapFrom(src => src.User != null ? src.User.DisplayName : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null));
        
        CreateMap<WithdrawalRequest, WithdrawalRequestDto>()
            .ForMember(dest => dest.UserDisplayName, opt => opt.MapFrom(src => src.User != null ? src.User.DisplayName : null))
            .ForMember(dest => dest.UserEmail, opt => opt.MapFrom(src => src.User != null ? src.User.Email : null))
            .ForMember(dest => dest.CancelledByOwner,
                opt => opt.MapFrom(src => WithdrawalMessages.IsCancelledByOwner(src.Status, src.ProcessedBy, src.UserId)))
            // The admin list fills these in (GetPendingWithdrawalRequestsQueryHandler).
            .ForMember(dest => dest.RequesterWithdrawable, opt => opt.Ignore())
            .ForMember(dest => dest.RecentEarningReversals, opt => opt.Ignore());
        
        // The history fills these in (GetMyTransactionHistoryQueryHandler).
        CreateMap<PointTransaction, PointTransactionDto>()
            .ForMember(dest => dest.NovelSlug, opt => opt.Ignore())
            .ForMember(dest => dest.NovelTitle, opt => opt.Ignore())
            .ForMember(dest => dest.GiftNameAr, opt => opt.Ignore());
    }
}
