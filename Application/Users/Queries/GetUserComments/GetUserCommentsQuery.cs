using Application.Common;
using Application.Users.DTOS;
using MediatR;

namespace Application.Users.Queries.GetUserComments;

/// <summary>
/// GET /api/User/{userName}/comments: a page of the member's comments on chapters and paragraphs, replies included,
/// newest first (#54).
/// </summary>
public class GetUserCommentsQuery(string userName, int pageNumber, int pageSize) : IRequest<PagedResult<ProfileCommentDTO>>
{
    public string UserName { get; } = userName;
    public int PageNumber { get; } = pageNumber;
    public int PageSize { get; } = pageSize;
}
