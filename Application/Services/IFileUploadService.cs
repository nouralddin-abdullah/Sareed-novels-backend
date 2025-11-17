namespace Application.Services;

public interface IFileUploadService
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string UserName);
    Task<bool> DeleteImageAsync(string fileName);
    Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string NovelName);
    Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterName);
    Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId);
    Task<string> UploadReadingListCoverImageAsync(Stream fileStream, string contentType, string readingListId);
    Task<string> UploadPostImageAsync(Stream fileStream, string contentType, string postId);
    Task<string> UploadEntityGalleryImageAsync(Stream fileStream, string contentType, string entityId);
    Task<string> UploadPaymentProofAsync(Stream fileStream, string contentType, string userId);
}
