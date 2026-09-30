using Application.Common;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetUserWorks;

public class GetUserWorksQuery(string userId, int pageNumber, int pageSize, bool withChapters = false) : IRequest<PagedResult<MyWorksDTO>>
{
    public string UserId { get; set; } = userId;
    public int PageNumber { get; set; } = pageNumber;
    public int PageSize { get; set; } = pageSize;
    /// <summary>Only the novels a reader can open, with at least one published chapter (#46); otherwise every public novel.</summary>
    public bool WithChapters { get; set; } = withChapters;
}
