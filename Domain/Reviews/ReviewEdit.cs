using Domain.Entities;

namespace Domain.Reviews;

/// <summary>
/// What the author of a review changes in it (#34). A null score or <see cref="IsSpoiler"/> is a field she didn't send,
/// which stays as it is. The text changes only when <see cref="ReplacesContent"/>, to <see cref="Content"/> (null: no
/// text). The id, likes and creation date never change.
/// </summary>
public sealed record ReviewEdit(
    decimal? WritingQualityScore,
    decimal? UpdatingStabilityScore,
    decimal? CharacterDevelopmentScore,
    decimal? WorldBuildingScore,
    bool ReplacesContent,
    string? Content,
    bool? IsSpoiler)
{
    /// <summary>
    /// The edit a request asks for, where a null field is one that wasn't sent. <paramref name="content"/> null keeps
    /// the text; empty or blank removes it, as a review may be written without text.
    /// </summary>
    public static ReviewEdit Of(
        decimal? writingQualityScore,
        decimal? updatingStabilityScore,
        decimal? characterDevelopmentScore,
        decimal? worldBuildingScore,
        string? content,
        bool? isSpoiler) =>
        new(writingQualityScore, updatingStabilityScore, characterDevelopmentScore, worldBuildingScore,
            ReplacesContent: content is not null,
            Content: string.IsNullOrWhiteSpace(content) ? null : content,
            isSpoiler);

    /// <summary>
    /// Whether this edit changes <paramref name="review"/>. Saving the form as it was is no edit, so it doesn't mark
    /// the review edited. Missing, empty and blank text are all no text.
    /// </summary>
    public bool Changes(Review review) =>
        Differs(WritingQualityScore, review.WritingQualityScore)
        || Differs(UpdatingStabilityScore, review.UpdatingStabilityScore)
        || Differs(CharacterDevelopmentScore, review.CharacterDevelopmentScore)
        || Differs(WorldBuildingScore, review.WorldBuildingScore)
        || (IsSpoiler is { } isSpoiler && isSpoiler != review.IsSpoiler)
        || (ReplacesContent && !SameText(Content, review.Content));

    private static bool Differs(decimal? sent, decimal current) => sent is { } value && value != current;

    private static bool SameText(string? a, string? b) =>
        string.IsNullOrWhiteSpace(a) ? string.IsNullOrWhiteSpace(b) : string.Equals(a, b, StringComparison.Ordinal);
}
