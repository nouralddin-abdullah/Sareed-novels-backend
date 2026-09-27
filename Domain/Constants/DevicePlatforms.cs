namespace Domain.Constants;

public static class DevicePlatforms
{
    public const string Android = "android";
    public const string Ios = "ios";

    public static readonly IReadOnlyCollection<string> All = [Android, Ios];
}
