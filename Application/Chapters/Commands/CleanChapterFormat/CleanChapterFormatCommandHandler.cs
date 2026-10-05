using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Application.Chapters.Commands.CleanChapterFormat.ChapterFormatPlan;

namespace Application.Chapters.Commands.CleanChapterFormat;

public class CleanChapterFormatCommandHandler(
    IServiceScopeFactory scopes,
    ILogger<CleanChapterFormatCommandHandler> logger) : IRequestHandler<CleanChapterFormatCommand, ChapterFormatReport>
{
    /// <summary>How long one call goes on before it stops, between two batches, and answers with nextCursor.</summary>
    public static readonly TimeSpan TimeBudget = TimeSpan.FromSeconds(20);

    public async Task<ChapterFormatReport> Handle(CleanChapterFormatCommand request, CancellationToken cancellationToken)
    {
        var report = new ChapterFormatReport { DryRun = request.DryRun };
        var batchSize = Math.Clamp(request.BatchSize, 1, CleanChapterFormatCommand.MaxBatchSize);
        var stopwatch = Stopwatch.StartNew();
        var cursor = request.After;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A scope (and so a database context) per batch: the pass reads every paragraph, and a context keeps what it
            // tracks.
            using var scope = scopes.CreateScope();
            var chapters = scope.ServiceProvider.GetRequiredService<IChaptersRepository>();
            var paragraphs = scope.ServiceProvider.GetRequiredService<IChapterParagraphsRepository>();

            var ids = await chapters.GetChapterIdsAsync(cursor, batchSize);
            if (ids.Count == 0)
            {
                cursor = null;
                break;
            }

            var stored = await paragraphs.GetParagraphsOfChaptersAsync(ids);
            var plans = ids.ToDictionary(id => id, id => For(stored[id]));
            var commented = await paragraphs.GetCommentedAsync(plans.Values.SelectMany(p => p.EmptyRows).ToList());
            foreach (var id in ids)
            {
                var plan = plans[id];
                plan.KeepCommented(commented);
                if (request.DryRun || !plan.HasChanges)
                {
                    Add(report, id, plan);
                    continue;
                }

                try
                {
                    Add(report, id, await Convert(paragraphs, id));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Nothing of this chapter was saved (one transaction); the next run tries it again.
                    logger.LogError(ex, "Converting chapter {ChapterId} to chapter format v1 failed", id);
                    report.ChaptersChecked++;
                    report.Failures.Add(new ChapterFailure { ChapterId = id, Error = ex.Message });
                }
            }

            cursor = ids[^1];
            if (ids.Count < batchSize)
            {
                cursor = null;
                break;
            }

            if (stopwatch.Elapsed > TimeBudget)
            {
                break;
            }
        }

        report.NextCursor = cursor;
        using (var scope = scopes.CreateScope())
        {
            var legacy = await scope.ServiceProvider.GetRequiredService<IChaptersRepository>().CountLegacyContentAsync();
            report.LegacyChapterContent = new LegacyChapterContentReport
            {
                Chapters = legacy.Chapters,
                WithoutParagraphs = legacy.WithoutParagraphs
            };
        }

        logger.LogInformation(
            "Chapter format v1 maintenance ({Mode}) from {After}: {Chapters} chapters, {Paragraphs} paragraphs checked; " +
            "{Rewritten} rewritten, {Split} split ({Added} added), {Removed} empty removed, {Hashes} hashes fixed, " +
            "{Skipped} skipped, {Failures} chapters failed; next {NextCursor}",
            request.DryRun ? "dry run" : "real pass", request.After, report.ChaptersChecked, report.ParagraphsChecked,
            report.ParagraphsRewritten, report.ParagraphsSplit, report.ParagraphsAdded, report.EmptyParagraphsRemoved,
            report.HashesFixed, report.ParagraphsSkipped, report.Failures.Count, report.NextCursor);
        return report;
    }

    /// <summary>
    /// Converts one chapter in its own edit of the chapter's text: its paragraphs read again under the chapter's text
    /// lock (an author may have saved it since the batch was read), planned again, and saved.
    /// </summary>
    private static async Task<ChapterFormatPlan> Convert(IChapterParagraphsRepository paragraphs, Guid chapterId)
    {
        await using var edit = await paragraphs.BeginEditAsync(chapterId);
        var plan = For(edit.Paragraphs);
        // The empty rows' comments are checked under locks held to the end, so none is added to them before they go.
        plan.KeepCommented(await edit.GetCommentedAsync(plan.EmptyRows.ToList()));
        if (plan.HasChanges)
        {
            var (kept, removed) = plan.Apply();
            await edit.SaveAsync(kept, removed);
        }

        return plan;
    }

    private static void Add(ChapterFormatReport report, Guid chapterId, ChapterFormatPlan plan)
    {
        report.ChaptersChecked++;
        if (plan.HasChanges)
        {
            report.ChaptersChanged++;
        }

        foreach (var row in plan.Rows)
        {
            report.ParagraphsChecked++;
            var conversion = row.Conversion;
            if (conversion.Pictures > 0)
            {
                report.Pictures.Paragraphs++;
                report.Pictures.Kept += conversion.PicturesKept;
                report.Pictures.NotKept += conversion.Pictures - conversion.PicturesKept;
                if (report.Pictures.Examples.Count < ChapterFormatReportLimits.PictureExamples)
                {
                    report.Pictures.Examples.Add(new PictureParagraph
                    {
                        ChapterId = chapterId, ParagraphId = row.Paragraph.Id, Pictures = conversion.Pictures,
                        Kept = conversion.PicturesKept, Content = Cut(row.Stored.Content)
                    });
                }
            }

            switch (row.Change)
            {
                case Change.None:
                    report.ParagraphsUnchanged++;
                    break;
                case Change.Skip:
                    report.ParagraphsSkipped++;
                    if (report.Skipped.Count < ChapterFormatReportLimits.Skipped)
                    {
                        report.Skipped.Add(new SkippedParagraph
                        {
                            ChapterId = chapterId, ParagraphId = row.Paragraph.Id, Reason = row.SkipReason!,
                            Before = Before(row.Stored), After = After(conversion), WordsBefore = Cut(row.WordsBefore),
                            WordsAfter = Cut(row.WordsAfter)
                        });
                    }

                    break;
                default:
                    var change = row.Change switch
                    {
                        Change.Rewrite => "rewritten",
                        Change.Split => "split",
                        Change.Remove => "emptyRemoved",
                        _ => "hashFixed"
                    };
                    switch (row.Change)
                    {
                        case Change.Rewrite:
                            report.ParagraphsRewritten++;
                            break;
                        case Change.Split:
                            report.ParagraphsSplit++;
                            report.ParagraphsAdded += conversion.Paragraphs.Count - 1;
                            break;
                        case Change.Remove:
                            report.EmptyParagraphsRemoved++;
                            break;
                        default:
                            report.HashesFixed++;
                            break;
                    }

                    if (report.Examples.Count(e => e.Change == change) < ChapterFormatReportLimits.ExamplesPerChange)
                    {
                        report.Examples.Add(new FormatExample
                        {
                            ChapterId = chapterId, ParagraphId = row.Paragraph.Id, Change = change,
                            Before = Before(row.Stored), After = After(conversion)
                        });
                    }

                    break;
            }
        }
    }

    private static FormatExampleParagraph Before(FormattedParagraph stored) => new()
    {
        Kind = stored.Kind, Content = Cut(stored.Content), Caption = stored.Caption
    };

    private static List<FormatExampleParagraph> After(StoredParagraphConversion conversion) =>
        conversion.Paragraphs.Select(p => new FormatExampleParagraph { Kind = p.Kind, Content = Cut(p.Content), Caption = p.Caption }).ToList();

    private static string Cut(string text) =>
        text.Length <= ChapterFormatReportLimits.ContentLength ? text : text[..ChapterFormatReportLimits.ContentLength] + "…";
}
