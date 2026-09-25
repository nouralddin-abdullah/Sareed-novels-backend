using Application.Services;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Covers.Commands.ConvertLegacyCovers;

public class ConvertLegacyCoversCommandHandler(
    INovelsRepository novelsRepository,
    INovelCoverService coverService,
    ILogger<ConvertLegacyCoversCommandHandler> logger) : IRequestHandler<ConvertLegacyCoversCommand, ConvertLegacyCoversResult>
{
    public async Task<ConvertLegacyCoversResult> Handle(ConvertLegacyCoversCommand request, CancellationToken cancellationToken)
    {
        var result = new ConvertLegacyCoversResult { DryRun = request.DryRun };
        if (!coverService.ProcessorAvailable)
        {
            result.ProcessorAvailable = false;
            result.Remaining = (await novelsRepository.CountCoversAsync(NovelCovers.StandardUrlMarker, cancellationToken)).NotMatching;
            return result;
        }

        var batchSize = Math.Clamp(request.BatchSize, 1, ConvertLegacyCoversCommand.MaxBatchSize);
        var batch = await novelsRepository.GetCoversNotMatchingAsync(NovelCovers.StandardUrlMarker, request.After, batchSize, cancellationToken);

        foreach (var novel in batch)
        {
            var item = new ConvertedCoverItem { NovelId = novel.Id, Title = novel.Title, OldUrl = novel.CoverImageUrl };
            result.Items.Add(item);
            try
            {
                var conversion = await coverService.ConvertExistingAsync(novel.Id, novel.CoverImageUrl, request.DryRun, cancellationToken);
                item.Layout = conversion.Mode;
                item.SourceWidth = conversion.SourceWidth;
                item.SourceHeight = conversion.SourceHeight;
                item.SourceBytes = conversion.SourceBytes;
                item.OutputWidth = conversion.OutputWidth;
                item.OutputBytes = conversion.OutputBytes;

                if (request.DryRun)
                {
                    item.Outcome = "wouldConvert";
                    continue;
                }

                item.NewUrl = conversion.NewUrl;
                if (await novelsRepository.SetCoverUrlAsync(novel.Id, conversion.NewUrl!, expectedUrl: novel.CoverImageUrl, cancellationToken))
                {
                    item.Outcome = "converted";
                    result.Converted++;
                    logger.LogInformation("Converted the cover of novel {NovelId} ({Layout}, {SourceBytes} -> {OutputBytes} bytes)",
                        novel.Id, conversion.Mode, conversion.SourceBytes, conversion.OutputBytes);
                }
                else
                {
                    // The author uploaded a new (already standard) cover while this ran; drop what was just made.
                    item.Outcome = "skipped";
                    item.NewUrl = null;
                    result.Skipped++;
                    await coverService.DeleteStandardCoverAsync(conversion.NewUrl!, cancellationToken);
                }
            }
            catch (CoverImageException ex)
            {
                item.Outcome = "failed";
                item.ErrorCode = ex.Code;
                item.Error = ex.Message;
                result.Failed++;
                logger.LogWarning("Could not convert the cover of novel {NovelId} ({Url}): {Code} {Message}", novel.Id, novel.CoverImageUrl, ex.Code, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Storage or database trouble: reported per cover, logged with the stack, and the batch goes on.
                item.Outcome = "failed";
                item.ErrorCode = "cover_conversion_error";
                item.Error = ex.Message;
                result.Failed++;
                logger.LogError(ex, "Converting the cover of novel {NovelId} failed", novel.Id);
            }
        }

        result.NextCursor = batch.Count == batchSize ? batch[^1].Id : null;
        result.Remaining = (await novelsRepository.CountCoversAsync(NovelCovers.StandardUrlMarker, cancellationToken)).NotMatching;
        return result;
    }
}
