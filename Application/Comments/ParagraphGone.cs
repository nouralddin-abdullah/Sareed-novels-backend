using Domain.Exceptions;

namespace Application.Comments;

/// <summary>
/// A paragraph comment's paragraph is gone: the author edited it, which gives it a new id and deletes its comments.
/// HTTP 404 with code <see cref="Code"/>; the app reloads the chapter.
/// </summary>
public static class ParagraphGone
{
    public const string Code = "ParagraphNotFound";

    public const string Message = "الفقرة غير موجودة، ربما عدّلها الكاتب. أعد فتح الفصل.";

    public static NotFoundException Exception() => new(Message, Code);
}
