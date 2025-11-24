namespace Infrastructure.Configuration;

public class OpenSearchSettings
{
    public const string SectionName = "OpenSearch";
    
    public string Url { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public string NovelIndexName { get; set; } = "sareed-novels";
}
