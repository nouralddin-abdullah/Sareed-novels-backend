using Application.Chapters.DTOS;
using Application.Users.Commands.FollowUser;
using MediatR;

namespace Application.Chapters.Commands.CreateChapter;

public class CreateChapterCommand(Guid novelId, string status, string title, string content) : IRequest<ChapterSingleAuthorDTO>
{
    public Guid NovelId { get; set; } = novelId;
    public Guid Id { get; set; }
    public string Status { get; set; } = status;
    public string Content { get; set; } = content;
    public string Title { get; set; } = title;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
