using System.Text.Json.Serialization;

namespace Application.Chapters.Commands.UpdateChapter;

public class UpdateChapterRequest
{
    public string? Title { get; set; }
    public string? Status { get; set; }
    public string? Content { get; set; }

    /// <summary>
    /// The draft's schedule (#77): a time to come (UTC, as <c>CreateChapterRequest.PublishAt</c>) schedules it or moves
    /// its schedule, <c>null</c> cancels it, and leaving the field out keeps the schedule as it is, as every edit does.
    /// </summary>
    public DateTime? PublishAt
    {
        get => publishAt;
        set
        {
            publishAt = value;
            PublishAtSent = true;
        }
    }

    /// <summary>Whether the body has <c>publishAt</c>, null included: only then does the save change the schedule.</summary>
    [JsonIgnore]
    public bool PublishAtSent { get; private set; }

    private DateTime? publishAt;
}
