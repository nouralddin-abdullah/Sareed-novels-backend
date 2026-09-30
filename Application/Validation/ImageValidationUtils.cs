using Microsoft.AspNetCore.Http;

namespace Application.Validation
{
    /// <summary>
    /// The pictures members upload (profile photos, reading lists, comments, posts...): JPEG, PNG or WebP, at most 5 MB.
    /// The type is the one the client declares for the file (its Content-Type); the bytes aren't inspected.
    /// </summary>
    public class ImageValidationUtils
    {
        /// <summary>The accepted types, exactly as written (image/jpg is a common alias of image/jpeg).</summary>
        public static readonly string[] AllowedImageTypes = { "image/jpeg", "image/png", "image/webp", "image/jpg" };
        public const int MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        public static bool IsAllowedType(string? contentType) => contentType is not null && AllowedImageTypes.Contains(contentType);

        public static bool IsWithinMaxSize(long length) => length <= MaxFileSizeBytes;

        public static bool IsValidImageFile(IFormFile? file) =>
            file == null || (IsAllowedType(file.ContentType) && IsWithinMaxSize(file.Length));
    }
}
