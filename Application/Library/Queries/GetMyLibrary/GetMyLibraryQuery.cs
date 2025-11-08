using Application.Common;
using Application.Library.DTOs;
using MediatR;

namespace Application.Library.Queries.GetMyLibrary;

public class GetMyLibraryQuery : IRequest<PagedResult<ReadingProgressDTO>>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
