using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Application.Users;

public interface IVisitorContext
{
    /// <summary>
    /// A stable key for "who is viewing" used to count unique daily views: "u:{userId}" when signed in, otherwise
    /// "a:{hash}" from a salted hash of IP + user agent (no raw IP is stored). Returns null for crawlers, link
    /// previews, scripts, and our own SEO worker, which must not count as readers.
    /// </summary>
    string? GetVisitorKey();
}

public partial class VisitorContext(IHttpContextAccessor httpContextAccessor, IConfiguration configuration) : IVisitorContext
{
    public string? GetVisitorKey()
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            return null;
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent) || NonReaderAgent().IsMatch(userAgent))
        {
            return null;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            return $"u:{userId}";
        }

        var ip = ClientIp(context);
        var salt = configuration["Tracking:Salt"] ?? configuration["Jwt:Key"] ?? "sard";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}|{ip}|{userAgent}"));
        return $"a:{Convert.ToHexString(hash, 0, 16)}";
    }

    private static string ClientIp(HttpContext context)
    {
        var headers = context.Request.Headers;
        var cloudflare = headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cloudflare))
        {
            return cloudflare.Trim();
        }

        var forwarded = headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            return forwarded.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    // Search/social crawlers, link unfurlers, headless browsers, HTTP libraries, and our Cloudflare SEO worker.
    // Deliberately doesn't match "Dart/" (the mobile app's HTTP client) or normal browsers.
    [GeneratedRegex(
        @"bot\b|bot/|crawl|spider|slurp|mediapartners|facebookexternalhit|embedly|preview|whatsapp|telegram|discord|" +
        @"headless|lighthouse|pagespeed|curl/|wget/|python|java/|go-http-client|node-fetch|undici|axios/|postman|insomnia|" +
        @"SardSeoWorker",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NonReaderAgent();
}
