using Application.Privileges.DTOs;
using MediatR;

namespace Application.Privileges.Queries.GetPrivilegeInfo;

public class GetPrivilegeInfoQuery : IRequest<PrivilegeInfoDto?>
{
    public Guid NovelId { get; set; }
}
