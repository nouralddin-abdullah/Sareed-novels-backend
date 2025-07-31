using Application.Novels.DTOS;
using MediatR;

namespace Application.Novels.Queries.GetWork;

public class GetWorkQuery(Guid workGuid) : IRequest<WorkDTO>
{
    public Guid WorkGuid { get; set; } = workGuid;
}
