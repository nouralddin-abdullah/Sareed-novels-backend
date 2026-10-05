using System.Diagnostics;
using Application.Chapters.Paragraphs;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Which saved paragraphs survive a chapter edit (and keep their id and comments): exactly those whose words are
/// unchanged, each copy of a repeated paragraph on its own, wherever the author moved them.
/// </summary>
public class ParagraphMatcherTests
{
    private const string P = "<p class=\"min-h-[1em]\">";
    private const string A = P + "كان الكسندر، صاحب دار النشر، رجلاً يرى الأعمال الأدبية بعين المستثمر والناقد معاً.";
    private const string B = P + "ز ألكسندر رأسه. \"حسناً يا فادي. ساقوم بنشر كتابك. لن يكون مجر كتاب، بل سيكون نداء جماهيريا.";
    private const string C = P + "وفي ليلة ممطرة، وبينما كان فادي يعمل في تنظيف أروقة الفندق، كان .الكسندر يراقب الاخبار في منزله";
    private const string D = P + "انتشرت الصورة في دقائق. معجبو ليام تساءلوا عن الكتاب. هل يقرأه ليام؟ ما قصته؟";
    private const string E = P + "في تلك الليلة، تركت نور عملها وحياتها البراقة والمزيفة.";
    private const string X = P + "\"نعم.\"";
    private const string Y = P + "صمت طويل.";

    /// <summary>For each edited paragraph, the saved index it continues (-1: new).</summary>
    private static int[] Pairs(ParagraphMatch match) => match.SavedIndexByEdited.ToArray();

    /// <summary>Matches paragraphs given as stored HTML (text paragraphs), each read as the API reads a stored one.</summary>
    private static ParagraphMatch Match(IEnumerable<string> saved, IEnumerable<string> edited) =>
        ParagraphMatcher.Match(Read(saved), Read(edited));

    private static List<FormattedParagraph> Read(IEnumerable<string> paragraphs) =>
        paragraphs.Select(html => ChapterFormat.Read(html, ParagraphKinds.Text, null)).ToList();

    [Fact]
    public void Saving_an_unchanged_chapter_keeps_every_paragraph_in_place()
    {
        var match = Match([A, B, C], [A, B, C]);

        Assert.Equal([0, 1, 2], Pairs(match));
        Assert.Empty(match.RemovedSaved);
        Assert.Equal((3, 0, 0, 0), (match.Kept, match.Moved, match.Created, match.Removed));
    }

    [Fact]
    public void A_typo_fix_makes_the_paragraph_new_and_removes_the_old_one()
    {
        var fixedB = B.Replace("مجر كتاب", "مجرد كتاب");

        var match = Match([A, B, C], [A, fixedB, C]);

        Assert.Equal([0, -1, 2], Pairs(match));
        Assert.Equal([1], match.RemovedSaved);
        Assert.Equal((2, 0, 1, 1), (match.Kept, match.Moved, match.Created, match.Removed));
    }

    [Theory]
    [InlineData("ألكسندر", "ألكسندرُ")] // a diacritic added
    [InlineData("حسناً", "حسنا")] // a diacritic removed
    [InlineData("رأسه.", "رأسه،")] // punctuation
    [InlineData("ساقوم", "سأقوم")] // a hamza
    public void A_one_character_change_is_a_change(string before, string after)
    {
        var match = Match([A, B], [A, B.Replace(before, after)]);

        Assert.Equal([0, -1], Pairs(match));
        Assert.Equal([1], match.RemovedSaved);
    }

    [Fact]
    public void Deleting_a_paragraph_removes_only_that_paragraph()
    {
        var match = Match([A, B, C, D], [A, C, D]);

        Assert.Equal([0, 2, 3], Pairs(match));
        Assert.Equal([1], match.RemovedSaved);
    }

    [Fact]
    public void Inserting_paragraphs_keeps_every_saved_one()
    {
        var match = Match([A, C], [D, A, B, C, E]);

        Assert.Equal([-1, 0, -1, 1, -1], Pairs(match));
        Assert.Empty(match.RemovedSaved);
        Assert.Equal((2, 0, 3, 0), (match.Kept, match.Moved, match.Created, match.Removed));
    }

    [Fact]
    public void Reordered_paragraphs_keep_their_ids()
    {
        var match = Match([A, B, C, D, E], [E, A, C, B, D]);

        Assert.Equal([4, 0, 2, 1, 3], Pairs(match));
        Assert.Empty(match.RemovedSaved);
        Assert.Equal(5, match.Kept + match.Moved);
        Assert.Equal(2, match.Moved); // E to the top, and B or C past the other
    }

    [Fact]
    public void Repeated_paragraphs_each_keep_their_own_id_through_an_unrelated_edit()
    {
        var match = Match([X, A, X, B, X], [Y, X, A.Replace("معاً", "معا"), X, B, X]);

        Assert.Equal([-1, 0, -1, 2, 3, 4], Pairs(match));
        Assert.Equal([1], match.RemovedSaved);
    }

    [Fact]
    public void Deleting_one_copy_of_a_repeated_paragraph_removes_that_copy()
    {
        Assert.Equal([0], Match([X, A, X], [A, X]).RemovedSaved);
        Assert.Equal([2], Match([X, A, X], [X, A]).RemovedSaved);
        Assert.Equal([1, 2], Pairs(Match([X, A, X], [A, X])));
        Assert.Equal([0, 1], Pairs(Match([X, A, X], [X, A])));
    }

    [Fact]
    public void Repeated_paragraphs_moved_around_keep_their_ids()
    {
        var match = Match([X, A, X, B], [B, X, A, X]);

        Assert.Equal([3, 0, 1, 2], Pairs(match));
        Assert.Equal((3, 1), (match.Kept, match.Moved));
    }

    [Fact]
    public void Adding_a_copy_of_a_paragraph_keeps_the_original_and_adds_a_new_one()
    {
        var match = Match([X, A], [X, A, X]);

        Assert.Equal([0, 1, -1], Pairs(match));
        Assert.Empty(match.RemovedSaved);
    }

    [Fact]
    public void Whitespace_and_formatting_changes_keep_the_paragraph()
    {
        string[] saved = [A, B, C, D, E];
        string[] edited =
        [
            // Spacing: leading, trailing, doubled, non-breaking
            "  " + P + Text(A).Replace(" ", "  ").Replace("،", "،&nbsp;") + "  ",
            // Another wrapper, bold and italic, and quotes written as character references
            "<p>" + Text(B).Replace("مجر كتاب", "<strong>مجر</strong> <em>كتاب</em>").Replace("\"", "&quot;") + "</p>",
            // Line endings between the words
            P + Text(C).Replace(" ", "\r\n"),
            // A line break instead of a space
            P + Text(D).Replace("؟ ", "؟<br>"),
            // Underline and a span
            P + Text(E).Replace("نور", "<u>نور</u>").Replace("والمزيفة", "<span dir=\"rtl\">والمزيفة</span>")
        ];

        var match = Match(saved, edited);

        Assert.Equal([0, 1, 2, 3, 4], Pairs(match));
        Assert.Empty(match.RemovedSaved);
    }

    [Fact]
    public void A_split_or_a_merge_is_a_change()
    {
        const string first = P + "كان الكسندر، صاحب دار النشر،";
        const string second = P + "رجلاً يرى الأعمال الأدبية بعين المستثمر والناقد معاً.";

        var split = Match([A, B], [first, second, B]);
        Assert.Equal([-1, -1, 1], Pairs(split));
        Assert.Equal([0], split.RemovedSaved);

        var merged = Match([first, second, B], [A, B]);
        Assert.Equal([-1, 2], Pairs(merged));
        Assert.Equal([0, 1], merged.RemovedSaved);
    }

    [Fact]
    public void Empty_chapters_on_either_side()
    {
        Assert.Equal([-1, -1], Pairs(Match([], [A, B])));
        Assert.Equal([0, 1], Match([A, B], []).RemovedSaved);
        Assert.Empty(Pairs(Match([], [])));
    }

    [Fact]
    public void The_production_chapter_saved_unchanged_keeps_everything()
    {
        var paragraphs = Integration.ProductionChapter.Paragraphs;

        var match = Match(paragraphs, paragraphs.ToList());

        Assert.Equal(Enumerable.Range(0, paragraphs.Length), Pairs(match));
    }

    [Fact]
    public void Chapters_past_the_in_order_limit_still_keep_every_unchanged_paragraph()
    {
        // 2,100 × 2,100 cells once the equal start and end are set aside: over the limit, so only the moved pass runs.
        var saved = Enumerable.Range(0, 2_100).Select(i => $"{P}فقرة رقم {i}").ToArray();
        var edited = saved.Reverse().Prepend(P + "جديدة").Append(P + "أخيرة").ToArray();
        Assert.True((long)saved.Length * edited.Length > ParagraphMatcher.InOrderCellLimit);

        var stopwatch = Stopwatch.StartNew();
        var match = Match(saved, edited);
        stopwatch.Stop();

        Assert.Empty(match.RemovedSaved);
        Assert.Equal(saved.Length, match.Kept + match.Moved);
        Assert.Equal(Enumerable.Range(0, saved.Length).Reverse(), Pairs(match)[1..^1]);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void A_long_chapter_with_scattered_edits_matches_quickly()
    {
        var random = new Random(15);
        var saved = Enumerable.Range(0, 800).Select(i => P + Sentence(random, 40) + $" ({i})").ToArray();
        var edited = saved.Select((text, i) => i % 7 == 3 ? text.Replace("(", "[") : text).ToList();
        edited.RemoveRange(100, 20);
        edited.InsertRange(400, saved[500..530]);

        var stopwatch = Stopwatch.StartNew();
        var match = Match(saved, edited);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
        AssertValid(saved, edited, match);
    }

    /// <summary>
    /// Random chapters built from a few distinct paragraphs (so many repeats) and random edits: every match joins
    /// equal text, no saved paragraph is used twice, and as many paragraphs survive as the texts allow.
    /// </summary>
    [Fact]
    public void Random_edits_always_keep_every_paragraph_whose_text_survives()
    {
        var random = new Random(2026_09_27);
        string[] pool = [A, B, C, D, E, X, Y, A.Replace(P, "<p>"), B.Replace("مجر", "مجرد")];

        for (var run = 0; run < 500; run++)
        {
            var saved = Enumerable.Range(0, random.Next(0, 25)).Select(_ => pool[random.Next(pool.Length)]).ToList();
            var edited = saved.ToList();
            for (var edit = random.Next(0, 8); edit > 0 && edited.Count > 0; edit--)
            {
                var at = random.Next(edited.Count);
                switch (random.Next(4))
                {
                    case 0: edited.RemoveAt(at); break;
                    case 1: edited.Insert(at, pool[random.Next(pool.Length)]); break;
                    case 2: edited[at] = edited[at] + "!"; break;
                    default:
                        var moved = edited[at];
                        edited.RemoveAt(at);
                        edited.Insert(random.Next(edited.Count + 1), moved);
                        break;
                }
            }

            var match = Match(saved, edited);

            AssertValid(saved, edited, match);
            Assert.Equal(Pairs(match), Pairs(Match(saved, edited)));
        }
    }

    private static string Text(string paragraph) => paragraph[P.Length..];

    private static void AssertValid(IReadOnlyList<string> saved, IReadOnlyList<string> edited, ParagraphMatch match)
    {
        var pairs = Pairs(match);
        Assert.Equal(edited.Count, pairs.Length);

        var used = pairs.Where(i => i >= 0).ToList();
        Assert.Equal(used.Count, used.Distinct().Count());
        for (var j = 0; j < pairs.Length; j++)
        {
            if (pairs[j] >= 0)
            {
                Assert.Equal(ParagraphText.VisibleText(saved[pairs[j]]), ParagraphText.VisibleText(edited[j]));
            }
        }

        Assert.Equal(Enumerable.Range(0, saved.Count).Except(used).Order(), match.RemovedSaved);
        Assert.Equal(used.Count, match.Kept + match.Moved);
        Assert.Equal(edited.Count - used.Count, match.Created);

        // As many survivors as possible: per text, as many as the smaller side has copies.
        var possible = saved.GroupBy(ParagraphText.VisibleText)
            .Sum(g => Math.Min(g.Count(), edited.Count(e => ParagraphText.VisibleText(e) == g.Key)));
        Assert.Equal(possible, used.Count);

        // The in-order ones really are in order.
        var inOrder = pairs.Where(i => i >= 0).ToList();
        Assert.True(LongestIncreasing(inOrder) >= match.Kept);
    }

    private static int LongestIncreasing(List<int> values)
    {
        var tails = new List<int>();
        foreach (var value in values)
        {
            var at = tails.BinarySearch(value);
            if (at < 0)
            {
                at = ~at;
            }

            if (at == tails.Count)
            {
                tails.Add(value);
            }
            else
            {
                tails[at] = value;
            }
        }

        return tails.Count;
    }

    private static string Sentence(Random random, int words)
    {
        string[] vocabulary = ["كان", "فادي", "نور", "الكتاب", "المدينة", "الليل", "قال", "صمت", "ثم", "رسالة", "البحر", "الخبز"];
        return string.Join(' ', Enumerable.Range(0, words).Select(_ => vocabulary[random.Next(vocabulary.Length)]));
    }
}
