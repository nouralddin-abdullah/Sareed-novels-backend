namespace Domain.Ranking;

public static class RankingTypes
{
    public const string Trending = "Trending";
    public const string AllTime = "AllTime";
    public const string TopRated = "TopRated";
    public const string New = "New";
    public const string NewArrivals = "NewArrivals";

    /// <summary>
    /// Maps what clients send to the stored name, ignoring case and separators: the web app sends "top_rated",
    /// "trending", "new"; older clients "TopRated", "AllTime", "NewArrivals". Returns null for unknown types.
    /// </summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var key = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch
        {
            "trending" or "trendingnow" => Trending,
            "alltime" or "alltimegreats" or "popular" => AllTime,
            "toprated" or "top" => TopRated,
            "new" or "newhot" => New,
            "newarrivals" or "latest" => NewArrivals,
            _ => null
        };
    }
}
