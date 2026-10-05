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

    /// <summary>Whether this save sets or cancels the draft's schedule (#77); false keeps it as it is.</summary>
    public bool SetsSchedule { get; init; }

    /// <summary>With <see cref="SetsSchedule"/>: when the draft publishes itself, or null to cancel its schedule.</summary>
    public DateTime? PublishAt { get; init; }
}
