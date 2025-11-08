namespace Infrastructure.Configuration;

public class ElasticsearchSettings
{
    public const string SectionName = "Elasticsearch";
    
    public string Url { get; set; } = default!; // Changed from CloudId
    public string ApiKey { get; set; } = default!;
    public string NovelIndexName { get; set; } = "sareed-novels";
}
