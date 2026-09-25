using Amazon.S3;
using Amazon.S3.Model;
using Infrastructure.Configuration;
using Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Sareed_novels_backend.Tests.Unit;

public class CloudflareR2ServiceTests
{
    [Theory]
    [InlineData(null, true)]                     // production: R2 over https, unsigned payload
    [InlineData("", true)]
    [InlineData("https://s3.local", true)]
    [InlineData("http://localhost:9000", false)] // local stand-in: the SDK only allows unsigned payloads over https
    public async Task Payload_signing_is_only_disabled_where_the_sdk_allows_it(string? serviceUrl, bool disabled)
    {
        var s3 = Substitute.For<IAmazonS3>();
        PutObjectRequest? sent = null;
        s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => sent = r), Arg.Any<CancellationToken>())
            .Returns(new PutObjectResponse());
        var settings = Options.Create(new CloudflareR2Settings
        {
            BucketName = "sard",
            PublicUrl = "https://pub-test.r2.dev",
            ServiceUrl = serviceUrl
        });

        var url = await new CloudflareR2Service(s3, settings)
            .UploadNovelImageAsync(new MemoryStream([1, 2, 3]), "image/png", Guid.NewGuid().ToString());

        Assert.NotNull(sent);
        Assert.Equal(disabled, sent!.DisablePayloadSigning);
        Assert.StartsWith("https://pub-test.r2.dev/novel-images/", url);
    }
}
