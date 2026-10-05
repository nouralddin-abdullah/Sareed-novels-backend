using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Novels.Commands.CreateNovel;

public class CreateNovelCommand : IRequest<CreateNovelResult>
{
    public string Title { get; set; } = default!;
    public string Summary { get; set; } = default!;
    public IFormFile CoverImageUrl { get; set; } = default!;
    public List<int> GenreIds { get; set; } = new List<int>();

    /// <summary>
    /// The form field <c>isDraft</c> (#76): true creates the novel as a draft, which only its author sees, as
    /// <c>PATCH /api/myworks/{id}/draft</c> makes one; she publishes it with <c>PATCH /api/myworks/{id}/publish</c>.
    /// Left out or false, the novel is public at once, as before.
    /// </summary>
    public bool IsDraft { get; set; }
}

public class CreateNovelResult : OperationResult
{
    public Guid? NovelId { get; set; }

    /// <summary>Whether the new novel is a draft (<see cref="CreateNovelCommand.IsDraft"/>); null when nothing was created.</summary>
    public bool? IsDraft { get; set; }

    /// <summary>Set when the cover was refused (see <see cref="Application.Covers.CoverErrorCodes"/>).</summary>
    public string? ErrorCode { get; set; }
}
