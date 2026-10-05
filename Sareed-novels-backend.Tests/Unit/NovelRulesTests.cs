using Application.Novels;
using Application.Novels.Commands.CreateNovel;
using Application.Novels.Commands.UpdateNovel;
using FluentValidation.Results;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// #76: an edit of a novel (<see cref="UpdateNovelCommandValidator"/>) refuses a title, summary or genre list that
/// creating one (<see cref="CreateNovelCommandValidator"/>) refuses, with the same messages, and accepts what it accepts.
/// </summary>
public class NovelRulesTests
{
    private static readonly CreateNovelCommandValidator Create = new();
    private static readonly UpdateNovelCommandValidator Update = new();

    public static TheoryData<string?> Titles() => new() { "", " ", "      ", "abc", new string('ع', 41), "عنوان", new string('ع', 40) };

    public static TheoryData<string?> Summaries() => new() { "", "      ", "abc", new string('ن', 2001), "نبذة", new string('ن', 2000) };

    public static TheoryData<int[]> GenreLists() =>
        new() { Array.Empty<int>(), new[] { 1, 1 }, new[] { 1, 2, 3, 4, 5 }, new[] { 1 }, new[] { 1, 2, 3, 4 } };

    [Theory]
    [MemberData(nameof(Titles))]
    public void A_title_gets_the_same_answer(string? title) =>
        AssertSame(nameof(CreateNovelCommand.Title),
            Create.Validate(new CreateNovelCommand { Title = title!, Summary = "نبذة", GenreIds = [1] }),
            Update.Validate(new UpdateNovelCommandRequest { Title = title }));

    [Theory]
    [MemberData(nameof(Summaries))]
    public void A_summary_gets_the_same_answer(string? summary) =>
        AssertSame(nameof(CreateNovelCommand.Summary),
            Create.Validate(new CreateNovelCommand { Title = "عنوان", Summary = summary!, GenreIds = [1] }),
            Update.Validate(new UpdateNovelCommandRequest { Summary = summary }));

    [Theory]
    [MemberData(nameof(GenreLists))]
    public void A_genre_list_gets_the_same_answer(int[] genreIds) =>
        AssertSame(nameof(CreateNovelCommand.GenreIds),
            Create.Validate(new CreateNovelCommand { Title = "عنوان", Summary = "نبذة", GenreIds = [.. genreIds] }),
            Update.Validate(new UpdateNovelCommandRequest { GenreIds = [.. genreIds] }));

    [Fact]
    public void A_blank_title_answers_one_message_and_a_short_one_the_length()
    {
        Assert.Equal([NovelRules.TitleRequiredMessage], Messages(Update.Validate(new UpdateNovelCommandRequest { Title = "     " }), "Title"));
        Assert.Equal([NovelRules.TitleRequiredMessage], Messages(Update.Validate(new UpdateNovelCommandRequest { Title = "" }), "Title"));
        Assert.Equal([NovelRules.TitleLengthMessage], Messages(Update.Validate(new UpdateNovelCommandRequest { Title = "abc" }), "Title"));
    }

    [Fact]
    public void Fields_an_edit_leaves_out_are_not_checked()
    {
        Assert.True(Update.Validate(new UpdateNovelCommandRequest()).IsValid);
    }

    [Fact]
    public void The_handlers_refuse_genres_with_the_validators_messages_and_unknown_ones_with_their_own()
    {
        var known = new HashSet<int> { 1, 2, 3, 4, 5 };
        Assert.Equal(NovelRules.GenresRequiredMessage, NovelRules.GenresRefusal([], known));
        Assert.Equal(NovelRules.GenresCountMessage, NovelRules.GenresRefusal([1, 2, 3, 4, 5], known));
        Assert.Equal(NovelRules.GenresDistinctMessage, NovelRules.GenresRefusal([1, 1], known));
        Assert.Equal(NovelRules.UnknownGenreMessage, NovelRules.GenresRefusal([1, 6], known));
        Assert.Null(NovelRules.GenresRefusal([1, 2, 3, 4], known));
    }

    private static void AssertSame(string field, ValidationResult created, ValidationResult edited) =>
        Assert.Equal(Messages(created, field), Messages(edited, field));

    private static List<string> Messages(ValidationResult result, string field) =>
        result.Errors.Where(e => e.PropertyName == field).Select(e => e.ErrorMessage).ToList();
}
