namespace Infrastructure.Configuration;

public class CloudflareR2Settings
{
    public const string SectionName = "CloudflareR2";
    public string AccessKey { get; set; } = default!;
    public string SecretKey { get; set; } = default!;
    public string BucketName { get; set; } = default!;
    public string AccountId { get; set; } = default!;
    public string PublicUrl { get; set; } = default!;

    /// <summary>Optional S3 endpoint override for local runs against a stand-in store; empty in production.</summary>
    public string? ServiceUrl { get; set; }

}
