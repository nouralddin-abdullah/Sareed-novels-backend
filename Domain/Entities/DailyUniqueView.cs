namespace Domain.Entities;

/// <summary>
/// One row per visitor per target per UTC day. Inserting a row that already exists is a no-op, which is what turns
/// raw page hits into unique daily views, and it's the source for "distinct readers in the last N days".
/// Rows older than the retention window are pruned; the daily totals live on in NovelViews / Chapter.ViewsCount.
/// </summary>
public class DailyUniqueView
{
    /// <summary>The novel id for <see cref="ViewKind.NovelPage"/>, the chapter id for <see cref="ViewKind.Chapter"/>.</summary>
    public Guid TargetId { get; set; }
    public DateTime Day { get; set; }
    /// <summary>"u:{userId}" for signed-in users, "a:{hash}" for guests (salted hash of IP + user agent).</summary>
    public string VisitorKey { get; set; } = default!;
    public Guid NovelId { get; set; }
    public ViewKind Kind { get; set; }
}

public enum ViewKind : byte
{
    NovelPage = 1,
    Chapter = 2
}
