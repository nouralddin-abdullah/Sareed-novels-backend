using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sareed_novels_backend.Tests.Integration;

/// <summary>
/// GET /api/app/config: the mobile apps' minimum versions (Android, iOS) and maintenance flag, from configuration, and
/// the limits the apps mirror (gift messages, posts).
/// </summary>
[Collection(ReaderApiCollection.Name)]
public class AppConfigHttpTests(SardApiFactory api)
{
    /// <summary>The API with these AppConfig settings on top of appsettings.json.</summary>
    private WebApplicationFactory<Program> With(Dictionary<string, string?> settings) =>
        api.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings)));

    [Fact]
    public async Task Anyone_gets_the_defaults_cacheable_for_five_minutes()
    {
        var response = await api.Get("/api/app/config");

        var config = await response.OkJson();
        Assert.Equal("1.0.0", config.GetProperty("android").GetProperty("minVersion").GetString());
        Assert.Equal("1.0.0", config.GetProperty("android").GetProperty("latestVersion").GetString());
        Assert.Equal("1.0.0", config.GetProperty("ios").GetProperty("minVersion").GetString());
        Assert.Equal("1.0.0", config.GetProperty("ios").GetProperty("latestVersion").GetString());
        Assert.False(config.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("maintenance").GetProperty("messageAr").ValueKind);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task Configured_values_are_served_and_changes_apply_without_a_restart()
    {
        await using var configured = With(new()
        {
            ["AppConfig:Android:MinVersion"] = "1.2.0",
            ["AppConfig:Android:LatestVersion"] = "1.3.10",
            ["AppConfig:Ios:MinVersion"] = "1.1.0",
            ["AppConfig:Ios:LatestVersion"] = "2.0.1",
            ["AppConfig:Maintenance:Enabled"] = "true",
            ["AppConfig:Maintenance:MessageAr"] = "سرد في صيانة قصيرة، نعود خلال ساعة"
        });

        var config = await (await configured.CreateClient().GetAsync("/api/app/config")).OkJson();

        Assert.Equal("1.2.0", config.GetProperty("android").GetProperty("minVersion").GetString());
        Assert.Equal("1.3.10", config.GetProperty("android").GetProperty("latestVersion").GetString());
        Assert.Equal("1.1.0", config.GetProperty("ios").GetProperty("minVersion").GetString());
        Assert.Equal("2.0.1", config.GetProperty("ios").GetProperty("latestVersion").GetString());
        Assert.True(config.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
        Assert.Equal("سرد في صيانة قصيرة، نعود خلال ساعة", config.GetProperty("maintenance").GetProperty("messageAr").GetString());

        // As when appsettings is edited on the server (or reloaded): the next request reads the new value.
        configured.Services.GetRequiredService<IConfiguration>()["AppConfig:Maintenance:Enabled"] = "false";
        var later = await (await configured.CreateClient().GetAsync("/api/app/config")).OkJson();
        Assert.False(later.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task The_post_limits_are_the_rules_posts_are_checked_against_whatever_the_configuration_says()
    {
        static void AssertPostRules(JsonElement config)
        {
            var posts = config.GetProperty("posts");
            Assert.Equal(5000, posts.GetProperty("contentMaxLength").GetInt32());
            Assert.Equal(5 * 1024 * 1024, posts.GetProperty("imageMaxBytes").GetInt64());
            Assert.Equal(["image/jpeg", "image/png", "image/webp"], posts.GetProperty("imageTypes").EnumerateArray().Select(t => t.GetString()));
        }

        AssertPostRules(await (await api.Get("/api/app/config")).OkJson());

        // They aren't settings: a section written by mistake changes nothing.
        await using var configured = With(new()
        {
            ["AppConfig:Posts:ContentMaxLength"] = "10",
            ["AppConfig:Posts:ImageMaxBytes"] = "1",
            ["AppConfig:Posts:ImageTypes:0"] = "image/gif"
        });
        AssertPostRules(await (await configured.CreateClient().GetAsync("/api/app/config")).OkJson());
    }

    [Theory]
    [InlineData("Android", "latest", "1.0.0")]
    [InlineData("Android", "1.2", "1.3.0")]
    [InlineData("Android", "2.0.0", "1.9.9")] // minimum above the latest
    [InlineData("Ios", "1.0", "1.0.0")]
    [InlineData("Ios", "3.0.0", "2.9.9")]
    public async Task A_misconfigured_version_is_an_error_not_a_wrong_answer(string platform, string minVersion, string latestVersion)
    {
        await using var misconfigured = With(new()
        {
            [$"AppConfig:{platform}:MinVersion"] = minVersion,
            [$"AppConfig:{platform}:LatestVersion"] = latestVersion
        });

        var response = await misconfigured.CreateClient().GetAsync("/api/app/config");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // Never cached (errors say no-store), so a fixed configuration reaches the apps at once.
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.CacheControl?.MaxAge);
    }
}
