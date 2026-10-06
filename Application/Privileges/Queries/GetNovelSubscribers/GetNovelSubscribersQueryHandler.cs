using Application.Privileges.DTOs;
using Application.Users;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;

namespace Application.Privileges.Queries.GetNovelSubscribers;

public class GetNovelSubscribersQueryHandler(
    IUserContext userContext,
    INovelsRepository novelsRepository,
    IPrivilegeSubscriptionRepository subscriptionRepository)
    : IRequestHandler<GetNovelSubscribersQuery, (IReadOnlyList<PrivilegeSubscriberDto> Subscribers, int TotalCount)>
{
    public async Task<(IReadOnlyList<PrivilegeSubscriberDto> Subscribers, int TotalCount)> Handle(GetNovelSubscribersQuery request,
        CancellationToken cancellationToken)
    {
        var currentUser = userContext.GetCurrentUser() ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");
        var novel = await novelsRepository.GetOne(request.NovelId) ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");
        if (novel.AuthorId != currentUser.Id)
        {
            throw new ForbidException("هذا الإجراء متاح لكاتب الرواية فقط", "NotOwner");
        }

        // Active subscriptions only (since #17 none is ever cancelled); a deleted account's went with it.
        var (subscriptions, totalCount) =
            await subscriptionRepository.GetNovelSubscribersAsync(request.NovelId, request.PageNumber, request.PageSize);
        var subscribers = subscriptions.Select(s => new PrivilegeSubscriberDto
        {
            UserId = s.UserId,
            UserName = s.User.UserName!,
            DisplayName = s.User.DisplayName,
            ProfilePhoto = s.User.ProfilePhoto,
            SubscribedAt = DateTime.SpecifyKind(s.SubscribedAt, DateTimeKind.Utc)
        }).ToList();
        return (subscribers, totalCount);
    }
}
