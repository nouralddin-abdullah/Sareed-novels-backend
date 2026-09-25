namespace Application.Services;

/// <summary>
/// Stores uploaded files. Every call writes a new object and returns its public URL; callers must save that URL.
/// Owner parameters are ids (never titles or usernames, which change and collide).
/// </summary>
public interface IFileUploadService
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string userId);
    Task<string> UploadProfileBannerAsync(Stream fileStream, string contentType, string userId);

    /// <summary>Deletes an object by its key or public URL. Returns false when it isn't ours or deletion failed.</summary>
    Task<bool> DeleteImageAsync(string keyOrUrl);

    Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string novelId);
    Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterId);
    Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId);
    Task<string> UploadReadingListCoverImageAsync(Stream fileStream, string contentType, string readingListId);
    Task<string> UploadPostImageAsync(Stream fileStream, string contentType, string postId);
    Task<string> UploadEntityGalleryImageAsync(Stream fileStream, string contentType, string entityId);
    Task<string> UploadPaymentProofAsync(Stream fileStream, string contentType, string userId);
    Task<string> UploadGiftImageAsync(Stream fileStream, string contentType, string giftId);
}
