using MediatR;

namespace Application.Covers.Commands.ConvertLegacyCovers;

/// <summary>
/// Converts up to <see cref="BatchSize"/> legacy covers (novels whose cover isn't in the <see cref="NovelCovers"/>
/// standard yet) and saves the new URLs. Idempotent: converted covers are no longer legacy, so running it again only
/// touches what is left. Pass <see cref="After"/> = the previous <c>nextCursor</c> to move past covers that failed;
/// a run without a cursor starts over and retries them.
/// </summary>
public class ConvertLegacyCoversCommand : IRequest<ConvertLegacyCoversResult>
{
    public const int MaxBatchSize = 25;

    public int BatchSize { get; init; } = 10;
    public Guid? After { get; init; }

    /// <summary>Read and process each cover, but store and change nothing.</summary>
    public bool DryRun { get; init; }
}

public class ConvertLegacyCoversResult
{
    public bool DryRun { get; set; }

    /// <summary>False when the image library can't run on this host; nothing was attempted.</summary>
    public bool ProcessorAvailable { get; set; } = true;

    public int Converted { get; set; }
    public int Failed { get; set; }

    /// <summary>Skipped because the author changed the cover while this ran (their new cover is already standard).</summary>
    public int Skipped { get; set; }

    /// <summary>Legacy covers left after this batch (including ones that failed).</summary>
    public int Remaining { get; set; }

    /// <summary>Pass as <c>after</c> to continue; null when this batch reached the end of the list.</summary>
    public Guid? NextCursor { get; set; }

    public List<ConvertedCoverItem> Items { get; set; } = [];
}

public class ConvertedCoverItem
{
    public Guid NovelId { get; set; }
    public string Title { get; set; } = default!;

    /// <summary>converted, wouldConvert (dry run), skipped or failed.</summary>
    public string Outcome { get; set; } = default!;

    public string OldUrl { get; set; } = default!;
    public string? NewUrl { get; set; }

    /// <summary>crop or fit (see Infrastructure CoverLayout).</summary>
    public string? Layout { get; set; }

    public int? SourceWidth { get; set; }
    public int? SourceHeight { get; set; }
    public long? SourceBytes { get; set; }
    public int? OutputWidth { get; set; }

    /// <summary>All files of the new cover together.</summary>
    public long? OutputBytes { get; set; }

    public string? ErrorCode { get; set; }
    public string? Error { get; set; }
}
