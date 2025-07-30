using Microsoft.AspNetCore.Http;

namespace Application.Validation
{
    public class ImageValidationUtils
    {
        public static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/webp", "image/jpg" };
        public const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        public static bool IsValidImageFile(IFormFile? file)
        {
            if (file == null) return true;
            return AllowedImageTypes.Contains(file.ContentType) && file.Length <= MaxFileSizeBytes;
        }
    }
}
