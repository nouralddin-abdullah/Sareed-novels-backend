using Application.Users.Commands.FollowUser;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Application.Novels.Commands.ChangeCover;

public class ChangerCoverCommand(Guid novelId, IFormFile coverImageUrl) : IRequest<ChangeCoverResult>
{

    public Guid NovelId { get; set; } = novelId;
    public IFormFile CoverImageUrl { get; set; } = coverImageUrl;
}

public class ChangeCoverResult : OperationResult
{
    /// <summary>The new cover URL (so the client can show it without refetching the novel).</summary>
    public string? CoverImageUrl { get; set; }

    /// <summary>Set when the cover was refused (see <see cref="Application.Covers.CoverErrorCodes"/>).</summary>
    public string? ErrorCode { get; set; }
}
