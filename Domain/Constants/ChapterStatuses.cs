namespace Domain.Constants;

public static class ChapterStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";

    public static readonly IReadOnlyCollection<string> All = [Draft, Published];
}
