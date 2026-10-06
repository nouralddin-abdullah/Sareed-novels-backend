using Application.Privileges.DTOs;
using MediatR;

namespace Application.Privileges.Queries.GetNovelSubscribers;

/// <summary>A page of the novel's early-access subscribers, newest first, for its author only (#96), and how many there are.</summary>
public record GetNovelSubscribersQuery(Guid NovelId, int PageNumber, int PageSize)
    : IRequest<(IReadOnlyList<PrivilegeSubscriberDto> Subscribers, int TotalCount)>;
