using Amazon.S3;
using Amazon.S3.Model;
using Application.Services;
using Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public class CloudflareR2Service(IAmazonS3 s3Client, IOptions<CloudflareR2Settings> settings) : IFileUploadService
{

    public async Task<string> UploadImageAsync(Stream fileStream, string fileName, string contentType, string UserName)
    {
        var key = $"profile-images/{UserName}";
        var request = new PutObjectRequest
        {
            BucketName = "sard",
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        await s3Client.PutObjectAsync(request);
        return $"{settings.Value.PublicUrl}/{key}";
    }

    public async Task<bool> DeleteImageAsync(string fileName)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = settings.Value.BucketName,
                Key = fileName
            };

            await s3Client.DeleteObjectAsync(request);
            return true;
        }
        catch
        {
            return false;
        }
    }

 

    public async Task<string> UploadNovelImageAsync(Stream fileStream, string contentType, string NovelName)
    {
        var key = $"novel-images/{NovelName}";
        var request = new PutObjectRequest
        {
            BucketName = "sard",
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        await s3Client.PutObjectAsync(request);
        return $"{settings.Value.PublicUrl}/{key}";
    }

    public async Task<string> UploadCharacterImageAsync(Stream fileStream, string contentType, string characterName)
    {
        var key = $"characters-images/{characterName}";
        var request = new PutObjectRequest
        {
            BucketName = "sard",
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        await s3Client.PutObjectAsync(request);
        return $"{settings.Value.PublicUrl}/{key}";
    }

    public async Task<string> UploadCommentImageAsync(Stream fileStream, string contentType, string commentId)
    {
        var key = $"comment-images/{commentId}";
        var request = new PutObjectRequest
        {
            BucketName = "sard",
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };
        await s3Client.PutObjectAsync(request);
        return $"{settings.Value.PublicUrl}/{key}";
    }
}
