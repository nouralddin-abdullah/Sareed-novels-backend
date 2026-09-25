using Amazon.S3;
using Amazon.S3.Model;
using Application.Services;
using Infrastructure.Configuration;
using Infrastructure.Services.Storage;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// Stores uploads in Cloudflare R2. Each upload is written once under a unique key (see <see cref="StorageKeys"/>)
/// with a long immutable cache lifetime; a changed image gets a new URL, which the caller saves.
/// </summary>
public class CloudflareR2Service(IAmazonS3 s3Client, IOptions<CloudflareR2Settings> settings) : IFileUploadService
{
    private const string ImmutableCache = "public, max-age=31536000, immutable";

    public Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string userId) =>
        PutAsync("profile-images", userId, fileStream, contentType);

    public Task<string> UploadProfileBannerAsync(Stream fileStream, string contentType, string userId) =>
        PutAsync("profile-banners", userId, fileStream, contentType);

    public async Task<bool> DeleteImageAsync(string keyOrUrl)
    {
        var key = StorageKeys.KeyFrom(settings.Value.PublicUrl, keyOrUrl);
        if (key is null) return false;
        try
        {
            await s3Client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = settings.Value.BucketName, Key = key });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string novelId) =>
        PutAsync("novel-images", novelId, fileStream, contentType);

    public Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterId) =>
        PutAsync("characters-images", characterId, fileStream, contentType);

    public Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId) =>
        PutAsync("comment-images", commentId, fileStream, contentType);

    public Task<string> UploadReadingListCoverImageAsync(Stream fileStream, string contentType, string readingListId) =>
        PutAsync("reading-list-images", readingListId, fileStream, contentType);

    public Task<string> UploadPostImageAsync(Stream fileStream, string contentType, string postId) =>
        PutAsync("post-images", postId, fileStream, contentType);

    public Task<string> UploadEntityGalleryImageAsync(Stream fileStream, string contentType, string entityId) =>
        PutAsync("entity-gallery", entityId, fileStream, contentType);

    public Task<string> UploadPaymentProofAsync(Stream fileStream, string contentType, string userId) =>
        PutAsync("payment-proofs", userId, fileStream, contentType);

    public Task<string> UploadGiftImageAsync(Stream fileStream, string contentType, string giftId) =>
        PutAsync("gift-images", giftId, fileStream, contentType);

    private async Task<string> PutAsync(string folder, string ownerId, Stream fileStream, string contentType)
    {
        var key = StorageKeys.NewKey(folder, ownerId, contentType);
        await s3Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = settings.Value.BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            Headers = { CacheControl = ImmutableCache },
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        });
        return StorageKeys.PublicUrl(settings.Value.PublicUrl, key);
    }
}
