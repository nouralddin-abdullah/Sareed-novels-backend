using Domain.Entities;
using Domain.Seo;

namespace Sareed_novels_backend.Tests.Unit;

public class WikiPagesTests
{
    // 180 Arabic letters: past the threshold on its own.
    private static readonly string LongText = string.Join(' ', Enumerable.Repeat("قائد ثوري شاب وذكي", 12));

    [Theory]
    [InlineData("ليون", true)]
    [InlineData("K", true)]
    [InlineData("  ليون  ", true)]
    [InlineData(".", false)]
    [InlineData(" - ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("_section_الشخصيات", false)]
    [InlineData("  _section_x", false)]
    public void Only_named_entries_count_as_real(string? name, bool expected)
    {
        Assert.Equal(expected, WikiPages.HasRealName(name));
    }

    [Fact]
    public void A_name_and_a_short_line_is_thin()
    {
        var entity = Entity("راع", shortDescription: "الحاكم الساقط");

        Assert.False(WikiPages.IsIndexable(entity));
    }

    [Fact]
    public void A_described_entry_is_indexable()
    {
        var entity = Entity("ليون", description: LongText);

        Assert.True(WikiPages.IsIndexable(entity));
    }

    [Fact]
    public void A_placeholder_row_is_never_indexable_whatever_its_text()
    {
        var entity = Entity("_section_الشخصيات", description: LongText);

        Assert.False(WikiPages.IsIndexable(entity));
    }

    [Fact]
    public void Letters_are_counted_across_every_field()
    {
        var count = WikiPages.LetterCount(
            shortDescription: "أبج",
            description: "د هـ",
            role: "و",
            attributesJson: """{"العمر": 25, "القوى": ["نار", "جليد"], "حي": true}""",
            articles: [("سيرة", "<p>ز&nbsp;ح</p>")]);

        // 3 + 3 + 1 + attributes (5 + 2 + 5 + 3 + 4 + 2) + article (4 + 2)
        Assert.Equal(34, count);
    }

    [Fact]
    public void Markup_and_json_escapes_are_not_counted_as_text()
    {
        // As System.Text.Json stores it by default: {"العمر": "١٢"} with every Arabic character escaped.
        var escaped = "{\"\\u0627\\u0644\\u0639\\u0645\\u0631\": \"\\u0661\\u0662\"}";

        Assert.Equal(7, WikiPages.LetterCount(null, null, null, escaped, []));
        Assert.Equal(2, WikiPages.LetterCount(null, null, null, null, [(null, "<p class=\"lead\"><strong>أب</strong></p>")]));
        Assert.Equal(0, WikiPages.LetterCount(null, null, null, "not json", []));
    }

    [Fact]
    public void Deleted_articles_do_not_count()
    {
        var entity = Entity("ليون");
        entity.Articles.Add(new EntityArticle { Title = "سيرة", Content = LongText, IsDeleted = true });

        Assert.False(WikiPages.IsIndexable(entity));

        entity.Articles.Add(new EntityArticle { Title = "سيرة", Content = $"<p>{LongText}</p>" });

        Assert.True(WikiPages.IsIndexable(entity));
    }

    [Fact]
    public void The_threshold_is_inclusive()
    {
        var atThreshold = new string('ب', WikiPages.MinLetters);

        Assert.True(WikiPages.IsIndexable(Entity("ليون", description: atThreshold)));
        Assert.False(WikiPages.IsIndexable(Entity("ليون", description: atThreshold[1..])));
    }

    private static NovelEntity Entity(string name, string? shortDescription = null, string? description = null) => new()
    {
        Id = Guid.NewGuid(),
        NovelId = Guid.NewGuid(),
        Section = "الشخصيات",
        Name = name,
        ShortDescription = shortDescription,
        Description = description
    };
}
