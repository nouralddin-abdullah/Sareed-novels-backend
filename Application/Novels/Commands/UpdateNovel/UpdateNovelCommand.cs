using Application.Users.Commands.FollowUser;
using Domain.Constants;
using MediatR;

namespace Application.Novels.Commands.UpdateNovel
{
    public class UpdateNovelCommand(Guid novelId, string? title, string? summary, string? status) : IRequest<OperationResult>
    {
        public Guid NovelId { get; set; } = novelId;
        public string? Title { get; set; } = title;
        public string? Summary { get; set; } = summary;
        public string? Status { get; set; } = status;
    }
}
