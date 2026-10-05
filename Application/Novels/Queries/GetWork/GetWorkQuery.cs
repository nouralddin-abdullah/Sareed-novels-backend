using Application.Chapters.Scheduling;
using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetWork;

public class GetWorkQuery(Guid workGuid) : IRequest<WorkDTO>, IReadsNovel
{
    public Guid WorkGuid { get; set; } = workGuid;

    Guid? IReadsNovel.ReadNovelId => WorkGuid;
}
