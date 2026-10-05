namespace Domain.Exceptions;

/// <summary>
/// A save of a chapter from a copy older than the chapter (#75): its baseRevision isn't the chapter's revision, because
/// the title or text was saved since, from another device or the web. Nothing is saved. HTTP 409 with JSON
/// <c>{"code": "ChapterChanged", "message", "revision"}</c>, <c>revision</c> the chapter's revision now.
/// </summary>
public sealed class ChapterChangedException(int revision) : ConflictException(ArabicMessage, ErrorCode)
{
    public const string ErrorCode = "ChapterChanged";

    public const string ArabicMessage = "حُفظ هذا الفصل من مكان آخر بعد أن فتحته. حمّل آخر نسخة منه قبل أن تحفظ.";

    /// <summary>The chapter's revision now.</summary>
    public int Revision { get; } = revision;
}
