namespace Application.Services;

public interface IFileUploadService
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string UserName);
    Task<bool> DeleteImageAsync(string fileName);
    Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string NovelName);

}
