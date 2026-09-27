namespace Infrastructure.Configuration;

/// <summary>
/// Firebase Cloud Messaging credentials. They come from host configuration only (environment variables
/// <c>Fcm__ServiceAccountJson</c> and optionally <c>Fcm__ProjectId</c>), never from appsettings files. Without them
/// push notifications are disabled; see README.md.
/// </summary>
public class FcmSettings
{
    public const string SectionName = "Fcm";

    /// <summary>The Firebase project id. Optional: defaults to the service account's <c>project_id</c>.</summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// The content of the service account's JSON key file, as is or base64-encoded (easier to paste where quotes need
    /// escaping, e.g. web.config).
    /// </summary>
    public string? ServiceAccountJson { get; set; }
}
