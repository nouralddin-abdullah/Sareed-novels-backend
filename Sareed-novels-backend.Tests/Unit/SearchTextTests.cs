using Domain.Search;

namespace Sareed_novels_backend.Tests.Unit;

public class SearchTextTests
{
    [Theory]
    [InlineData("أحمد", "احمد")]
    [InlineData("إسلام", "اسلام")]
    [InlineData("آمال", "امال")]
    [InlineData("ٱلكتاب", "الكتاب")]
    [InlineData("مدرسة", "مدرسه")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("فارسی", "فارسي")]
    [InlineData("کتاب", "كتاب")]
    public void Unifies_letter_variants_readers_treat_as_the_same(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Theory]
    [InlineData("مُمَوَّلٌ", "ممول")]          // tashkeel
    [InlineData("ســـرد", "سرد")]            // tatweel
    [InlineData("سر‌د", "سرد")]          // zero-width non-joiner
    [InlineData("الرَّحْمٰن", "الرحمن")]       // superscript alef
    public void Drops_marks_that_do_not_change_the_word(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Theory]
    [InlineData("الفصل ١٢", "الفصل 12")]
    [InlineData("الفصل ۱۲", "الفصل 12")]
    [InlineData("Zelvara", "zelvara")]
    [InlineData("  شيخ   في محراب قلبي ( مكتملة )  ", "شيخ في محراب قلبي مكتمله")]
    [InlineData("دارك:حكاية كيانين", "دارك حكايه كيانين")]
    [InlineData("hmot_mbdon", "hmot mbdon")]
    [InlineData("كيف؟ لماذا! ،نعم", "كيف لماذا نعم")]
    public void Normalizes_digits_case_punctuation_and_spacing(string input, string expected) =>
        Assert.Equal(expected, SearchText.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ...")]
    public void Empty_or_symbol_only_input_normalizes_to_empty(string? input) =>
        Assert.Equal(string.Empty, SearchText.Normalize(input));

    [Fact]
    public void Respects_max_length_without_a_trailing_space()
    {
        var result = SearchText.Normalize("abcd efgh ijkl", maxLength: 5);
        Assert.Equal("abcd", result);
        Assert.True(result.Length <= 5);
    }

    [Fact]
    public void Tokens_are_distinct_normalized_and_capped_at_six()
    {
        var tokens = SearchText.Tokens("المدرسة المدرسه أحمد a b c d e f");
        Assert.Equal(new[] { "المدرسه", "احمد", "a", "b", "c", "d" }, tokens);
    }

    [Fact]
    public void User_search_name_covers_display_name_and_user_name() =>
        Assert.Equal("احمد الكاتب ahmed writer", SearchText.ForUser("أحمد الكاتب", "Ahmed_Writer"));
}
