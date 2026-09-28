using Application.Services;
using Application.Users;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Wallet;

namespace Application.Gifts.Commands.SendGift;

public class SendGiftCommandHandler(
    ILogger<SendGiftCommandHandler> logger,
    IGiftRepository giftRepository,
    IGiftTransactionRepository giftTransactionRepository,
    INovelsRepository novelsRepository,
    IUserContext userContext,
    IWalletService walletService,
    ITransactionManager transactionManager,
    IServiceScopeFactory scopeFactory) : IRequestHandler<SendGiftCommand, OperationResult>
{
    public async Task<OperationResult> Handle(SendGiftCommand request, CancellationToken cancellationToken)
    {
        logger.LogInformation("User sending gift: GiftId={GiftId}, NovelId={NovelId}, Count={Count}",
            request.GiftId, request.NovelId, request.Count);

        // Validation
        if (request.Count < 1 || request.Count > 100)
        {
            return new OperationResult
            {
                Success = false,
                Code = "InvalidGiftCount",
                Message = "عدد الهدايا يجب أن يكون من 1 إلى 100"
            };
        }

        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("سجّل الدخول للمتابعة", "NotSignedIn");

        var gift = await giftRepository.GetGiftById(request.GiftId)
            ?? throw new NotFoundException("الهدية غير موجودة", "GiftNotFound");

        if (!gift.IsActive)
        {
            return new OperationResult
            {
                Success = false,
                Code = "GiftUnavailable",
                Message = "هذه الهدية لم تعد متاحة"
            };
        }

        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("الرواية غير موجودة", "NovelNotFound");

        // Prevent users from gifting their own novels
        if (novel.AuthorId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Code = "CannotGiftOwnNovel",
                Message = "لا يمكنك إرسال هدية إلى روايتك"
            };
        }

        var totalCost = gift.Cost * request.Count;

        // Check if user has sufficient balance
        var hasSufficientBalance = await walletService.HasSufficientBalanceAsync(currentUser.Id, totalCost);
        if (!hasSufficientBalance)
        {
            return new OperationResult
            {
                Success = false,
                Code = "InsufficientBalance",
                Message = "رصيدك من النقاط غير كافٍ"
            };
        }

        GiftTransaction giftTransaction;
        try
        {
            // Payment and gift record commit together on this request's DbContext. (The transaction used to be opened
            // on a DbContext from a new scope, so it covered none of these writes.)
            // Both ledger rows point at the gift record: that pairs the reader's payment with the author's earning, which
            // a refund of the points behind it takes back while it is still on hold (#22).
            var giftTransactionId = Guid.NewGuid();
            giftTransaction = await transactionManager.InTransactionAsync(async () =>
            {
                await walletService.TransferPointsAsync(
                    fromUserId: currentUser.Id,
                    toUserId: novel.AuthorId,
                    amount: totalCost,
                    fromTransactionType: TransactionType.GiftSent,
                    toTransactionType: TransactionType.GiftReceived,
                    fromDescription: TransactionDescriptions.GiftSent(gift.NameAr, request.Count, novel.Title),
                    // The author's wallet names the sender by display name: user names used to be email addresses.
                    toDescription: TransactionDescriptions.GiftReceived(gift.NameAr, request.Count, currentUser.DisplayName, novel.Title),
                    relatedRequestId: giftTransactionId,
                    details: new TransactionDetails(NovelId: novel.Id, GiftId: gift.Id, GiftCount: request.Count)
                );

                var record = new GiftTransaction
                {
                    Id = giftTransactionId,
                    GiftId = request.GiftId,
                    NovelId = request.NovelId,
                    SenderId = currentUser.Id,
                    Count = request.Count,
                    TotalCost = totalCost,
                    CreatedAt = DateTime.UtcNow
                };
                await giftTransactionRepository.CreateTransaction(record);
                return record;
            }, cancellationToken);
        }
        catch (InsufficientBalanceException)
        {
            // Another request spent the balance between the pre-check and the debit.
            return new OperationResult
            {
                Success = false,
                Code = "InsufficientBalance",
                Message = "رصيدك من النقاط غير كافٍ"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send gift: {Message}", ex.Message);

            return new OperationResult
            {
                Success = false,
                Code = "OperationFailed",
                Message = "تعذّر إرسال الهدية، ولم تُخصم أي نقاط. حاول مرة أخرى."
            };
        }

        logger.LogInformation(
            "Gift sent successfully: TransactionId={TransactionId}, From={SenderId}, To={AuthorId}, Amount={Amount}",
            giftTransaction.Id, currentUser.Id, novel.AuthorId, totalCost);

        // Notify AFTER the commit (best effort). The task outlives the request, so it gets its own scope from the
        // root scope factory rather than from the request's (soon disposed) provider.
        _ = Task.Run(async () =>
        {
            try
            {
                await SendGiftNotificationInBackground(novel.AuthorId, currentUser.Id, novel, gift, request.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send gift notification (non-critical)");
            }
        }, CancellationToken.None);

        return new OperationResult
        {
            Success = true,
            Message = $"أرسلت {request.Count}× {gift.NameAr} إلى رواية «{novel.Title}»"
        };
    }

    private async Task SendGiftNotificationInBackground(
        string authorId,
        string senderId,
        Novel novel,
        Gift gift,
        int count)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var usersRepository = scope.ServiceProvider.GetRequiredService<IUsersRepository>();

            var sender = await usersRepository.GetUserById(senderId);
            if (sender == null) return;

            // Send notification to novel author
            await notificationService.SendGiftReceivedNotification(
                authorId,
                sender,
                novel,
                gift,
                count
            );

            logger.LogDebug("Sent gift notification to author {AuthorId}", authorId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send gift notification");
        }
    }
}
