using Application.Services;
using Application.Users;
using Domain.Constants;
using Domain.Entities;
using Domain.Exceptions;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
                Message = "Count must be between 1 and 100"
            };
        }

        var currentUser = userContext.GetCurrentUser()
            ?? throw new ForbidException("User not authenticated");

        var gift = await giftRepository.GetGiftById(request.GiftId)
            ?? throw new NotFoundException("Gift not found");

        if (!gift.IsActive)
        {
            return new OperationResult
            {
                Success = false,
                Message = "This gift is no longer available"
            };
        }

        var novel = await novelsRepository.GetOne(request.NovelId)
            ?? throw new NotFoundException("Novel not found");

        // Prevent users from gifting their own novels
        if (novel.AuthorId == currentUser.Id)
        {
            return new OperationResult
            {
                Success = false,
                Message = "You cannot gift your own novel"
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
                Message = "Insufficient points balance"
            };
        }

        GiftTransaction giftTransaction;
        try
        {
            // Payment and gift record commit together on this request's DbContext. (The transaction used to be opened
            // on a DbContext from a new scope, so it covered none of these writes.)
            giftTransaction = await transactionManager.InTransactionAsync(async () =>
            {
                await walletService.TransferPointsAsync(
                    fromUserId: currentUser.Id,
                    toUserId: novel.AuthorId,
                    amount: totalCost,
                    fromTransactionType: TransactionType.GiftSent,
                    toTransactionType: TransactionType.GiftReceived,
                    fromDescription: $"Sent {request.Count}x {gift.Name} to {novel.Title}",
                    toDescription: $"Received {request.Count}x {gift.Name} from {currentUser.UserName} on {novel.Title}"
                );

                var record = new GiftTransaction
                {
                    Id = Guid.NewGuid(),
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
                Message = "Insufficient points balance"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send gift: {Message}", ex.Message);

            return new OperationResult
            {
                Success = false,
                Message = "Failed to send gift. No points were deducted. Please try again."
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
            Message = $"Successfully sent {request.Count}x {gift.Name} to {novel.Title}!"
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
