using AutoMapper;
using Domain.Entities;

namespace Application.Comments.DTOS;

public class CommentProfiles : Profile
{
    public CommentProfiles()
    {
        CreateMap<User, CommentUserDTO>();

        CreateMap<Domain.Entities.Comments, CommentsDTO>()
            .ForMember(dest => dest.IsLikedByCurrentUser, opt => opt.Ignore())
            .ForMember(dest => dest.TotalRepliesCount, opt => opt.Ignore())
            .ForMember(dest => dest.HasMoreReplies, opt => opt.Ignore());

        CreateMap<Domain.Entities.Comments, CommentReplyDTO>()
            .ForMember(dest => dest.IsLikedByCurrentUser, opt => opt.Ignore());
    }
}
