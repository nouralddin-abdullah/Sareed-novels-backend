using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Infrastructure.Configuration;

namespace Infrastructure.Push;

/// <summary>Gives the FCM HTTP v1 API an OAuth access token for each request.</summary>
public interface IFcmAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The Firebase project to send through and its credentials, read once at startup; or, when they're missing or
/// invalid, why push is disabled.
/// </summary>
public sealed class FcmConnection
{
    private FcmConnection(string? projectId, IFcmAccessTokenSource? tokens, string? disabledReason)
    {
        ProjectId = projectId;
        Tokens = tokens;
        DisabledReason = disabledReason;
    }

    public string? ProjectId { get; }
    public IFcmAccessTokenSource? Tokens { get; }
    public string? DisabledReason { get; }
    public bool IsEnabled => Tokens is not null;

    public static FcmConnection Enabled(string projectId, IFcmAccessTokenSource tokens) => new(projectId, tokens, null);

    public static FcmConnection Disabled(string reason) => new(null, null, reason);

    public static FcmConnection FromSettings(FcmSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ServiceAccountJson))
        {
            return Disabled("Fcm:ServiceAccountJson is not set");
        }

        string? keyProjectId;
        ServiceAccountCredential credential;
        try
        {
            var json = DecodeKey(settings.ServiceAccountJson);
            using (var document = JsonDocument.Parse(json))
            {
                keyProjectId = document.RootElement.TryGetProperty("project_id", out var id) ? id.GetString() : null;
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            credential = ServiceAccountCredential.FromServiceAccountData(stream);
        }
        catch (Exception ex)
        {
            return Disabled($"Fcm:ServiceAccountJson is not a valid service account key ({ex.GetType().Name}: {ex.Message})");
        }

        var projectId = string.IsNullOrWhiteSpace(settings.ProjectId) ? keyProjectId : settings.ProjectId.Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return Disabled("no Firebase project id: set Fcm:ProjectId");
        }
        return Enabled(projectId, new ServiceAccountTokenSource(credential));
    }

    /// <summary>The key file's JSON, pasted as is or base64-encoded.</summary>
    private static string DecodeKey(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('{') ? trimmed : Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
    }

    private sealed class ServiceAccountTokenSource(ServiceAccountCredential credential) : IFcmAccessTokenSource
    {
        private const string MessagingScope = "https://www.googleapis.com/auth/firebase.messaging";

        // Caches the token and refreshes it shortly before it expires (tokens last an hour).
        private readonly ITokenAccess scoped = GoogleCredential.FromServiceAccountCredential(credential).CreateScoped(MessagingScope);

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            scoped.GetAccessTokenForRequestAsync(authUri: null, cancellationToken);
    }
}
