using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Infrastructure.Configuration;

namespace Infrastructure.PlayBilling;

/// <summary>Gives the Google Play Developer API an OAuth access token for each request.</summary>
public interface IPlayAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>A point pack: a Play product id and the points it credits.</summary>
public sealed record PlayProduct(string ProductId, int Points);

/// <summary>
/// The app, catalog and credentials Play Billing runs with, read once at startup; or, when the credentials are missing
/// or invalid, why billing is disabled (then the catalog is empty and purchases answer BillingUnavailable).
/// </summary>
public sealed partial class PlayBillingConnection
{
    public const string DefaultPackageName = "com.sardnovels.app";

    private readonly Dictionary<string, int> pointsByProduct;

    private PlayBillingConnection(string packageName, IReadOnlyList<PlayProduct> products, IReadOnlyList<string> ignoredProducts,
        bool allowTestPurchases, IPlayAccessTokenSource? tokens, string? disabledReason)
    {
        PackageName = packageName;
        Products = products;
        IgnoredProducts = ignoredProducts;
        AllowTestPurchases = allowTestPurchases;
        Tokens = tokens;
        DisabledReason = disabledReason;
        pointsByProduct = products.ToDictionary(p => p.ProductId, p => p.Points, StringComparer.Ordinal);
    }

    public string PackageName { get; }

    /// <summary>Sorted by points.</summary>
    public IReadOnlyList<PlayProduct> Products { get; }

    /// <summary>Configured "id=points" entries left out: the id isn't a valid Play product id, or the points aren't a number.</summary>
    public IReadOnlyList<string> IgnoredProducts { get; }

    public bool AllowTestPurchases { get; }
    public IPlayAccessTokenSource? Tokens { get; }
    public string? DisabledReason { get; }
    public bool IsEnabled => Tokens is not null;

    public int? PointsFor(string productId) => pointsByProduct.TryGetValue(productId, out var points) ? points : null;

    public static PlayBillingConnection Enabled(string packageName, IEnumerable<PlayProduct> products, IPlayAccessTokenSource tokens,
        bool allowTestPurchases = false) =>
        new(packageName, products.OrderBy(p => p.Points).ToList(), Array.Empty<string>(), allowTestPurchases, tokens, null);

    public static PlayBillingConnection Disabled(string reason) =>
        new(DefaultPackageName, Array.Empty<PlayProduct>(), Array.Empty<string>(), false, null, reason);

    public static PlayBillingConnection FromSettings(PlayBillingSettings settings)
    {
        var packageName = string.IsNullOrWhiteSpace(settings.PackageName) ? DefaultPackageName : settings.PackageName.Trim();
        if (!PackageNamePattern().IsMatch(packageName))
        {
            return Disabled($"PlayBilling:PackageName '{packageName}' is not an Android package name");
        }

        var products = new List<PlayProduct>();
        var ignored = new List<string>();
        foreach (var (productId, value) in settings.Products)
        {
            if (productId.Length > Domain.Entities.PlayPurchase.ProductIdMaxLength || !ProductIdPattern().IsMatch(productId)
                || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var points))
            {
                ignored.Add($"{productId}={value}");
            }
            else if (points > 0)
            {
                products.Add(new PlayProduct(productId, points));
            }
            // 0 (or less) is how a default product is switched off: not worth a warning.
        }
        products.Sort((a, b) => a.Points.CompareTo(b.Points));
        if (products.Count == 0)
        {
            return Disabled("PlayBilling:Products has no product with positive points");
        }

        if (string.IsNullOrWhiteSpace(settings.ServiceAccountJson))
        {
            return Disabled("PlayBilling:ServiceAccountJson is not set");
        }

        ServiceAccountCredential credential;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(DecodeKey(settings.ServiceAccountJson)));
            credential = ServiceAccountCredential.FromServiceAccountData(stream);
        }
        catch (Exception ex)
        {
            // The messages name the problem (bad base64, not a service account key...), never the key's content.
            return Disabled($"PlayBilling:ServiceAccountJson is not a valid service account key ({ex.GetType().Name}: {ex.Message})");
        }

        return new PlayBillingConnection(packageName, products, ignored, settings.AllowTestPurchases,
            new ServiceAccountTokenSource(credential), null);
    }

    /// <summary>The key file's JSON, pasted as is or base64-encoded.</summary>
    private static string DecodeKey(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('{') ? trimmed : Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
    }

    // Play: starts with a lowercase letter or digit; lowercase letters, digits, underscores and periods.
    [GeneratedRegex("^[a-z0-9][a-z0-9_.]*$")]
    private static partial Regex ProductIdPattern();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$")]
    private static partial Regex PackageNamePattern();

    private sealed class ServiceAccountTokenSource(ServiceAccountCredential credential) : IPlayAccessTokenSource
    {
        private const string AndroidPublisherScope = "https://www.googleapis.com/auth/androidpublisher";

        // Caches the token and refreshes it shortly before it expires (tokens last an hour).
        private readonly ITokenAccess scoped = GoogleCredential.FromServiceAccountCredential(credential).CreateScoped(AndroidPublisherScope);

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await scoped.GetAccessTokenForRequestAsync(authUri: null, cancellationToken);
            }
            catch (TokenResponseException ex)
            {
                // Google turned the key down (deleted or disabled key, deleted account): a setup problem, not an outage.
                throw new PlayApiException($"Google refused the service account key: {ex.Error?.Error} {ex.Error?.ErrorDescription}",
                    statusCode: null, reason: ex.Error?.Error, isConfigurationError: true, ex);
            }
        }
    }
}
