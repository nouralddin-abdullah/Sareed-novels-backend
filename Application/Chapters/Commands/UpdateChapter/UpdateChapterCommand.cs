using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterCommand(Guid chapterId, Guid novelId, string? title, string? status, string? content) : IRequest<OperationResult>
{
    public Guid ChapterId { get; set; } = chapterId;
    public Guid NovelId { get; set; } = novelId;
    public string? Title { get; set; } = title;
    public string? Status { get; set; } = status;
    public string? Content { get; set; } = content;
}
