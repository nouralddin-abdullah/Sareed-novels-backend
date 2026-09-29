using Application.Common;
using Application.ReadingLists.DTOs;
using MediatR;

namespace Application.ReadingLists.Queries.GetMyReadingLists;

public class GetMyReadingListsQuery : IRequest<PagedResult<ReadingListPreviewDTO>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 12;

    /// <summary>A novel to ask about: each list then says whether it has it (containsNovel), for «أضف إلى قائمة».</summary>
    public Guid? ContainsNovelId { get; set; }
}
