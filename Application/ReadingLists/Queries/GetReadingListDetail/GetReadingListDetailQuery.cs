using Application.ReadingLists.DTOs;
using MediatR;

namespace Application.ReadingLists.Queries.GetReadingListDetail;

public class GetReadingListDetailQuery(Guid readingListId) : IRequest<ReadingListDetailDTO>
{
    public Guid ReadingListId { get; set; } = readingListId;
}
