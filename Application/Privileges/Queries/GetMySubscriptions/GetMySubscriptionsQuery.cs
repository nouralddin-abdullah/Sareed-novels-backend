using Application.Privileges.DTOs;
using MediatR;

namespace Application.Privileges.Queries.GetMySubscriptions;

public class GetMySubscriptionsQuery : IRequest<(List<PrivilegeSubscriptionDto>, int)>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
