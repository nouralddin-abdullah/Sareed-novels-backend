using System.Net;
using System.Text.RegularExpressions;
using Application.Chapters.Paragraphs;
using Domain.Constants;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// Chapter format v1 (#74): whatever a client sends, the server keeps only the editor's formatting, as paragraphs
/// with a kind (text, center, quote, break, image) and inline content (text, strong, em, u, s, br).
/// </summary>
public partial class ChapterFormatTests
{
    /// <summary>Inline content as format v1 allows it: text with &amp;amp; &amp;lt; &amp;gt; &amp;nbsp;, the four marks and br.</summary>
    [GeneratedRegex("^(?:[^<>&]|&(?:amp|lt|gt|nbsp);|</?(?:strong|em|u|s)>|<br>)*$")]
    private static partial Regex InlineV1();

    private static List<(string Kind, string Content, string? Caption)> Parse(string content) =>
        ChapterFormat.Parse(content).Select(p => (p.Kind, p.Content, p.Caption)).ToList();

    private static (string, string, string?) Text(string content) => (ParagraphKinds.Text, content, null);

    private static readonly (string, string, string?) Break = (ParagraphKinds.Break, "* * *", null);

    // ---- Each kind on the wire ----

    [Fact]
    public void Each_kind_on_the_wire_becomes_its_paragraph()
    {
        var parsed = Parse(
            "<p>فقرة عادية</p>" +
            "<p data-kind=\"center\">أبيات في الوسط</p>" +
            "<p data-kind=\"quote\">رسالة من بعيد</p>" +
            "<p data-kind=\"break\"></p>" +
            "<hr>" +
            "<p data-kind=\"image\"><img src=\"https://files.test/a.png\">خريطة المدينة</p>" +
            "<p data-kind=\"text\">نص صريح</p>");

        Assert.Equal(
        [
            Text("فقرة عادية"),
            (ParagraphKinds.Center, "أبيات في الوسط", null),
            (ParagraphKinds.Quote, "رسالة من بعيد", null),
            Break,
            Break,
            (ParagraphKinds.Image, "https://files.test/a.png", "خريطة المدينة"),
            Text("نص صريح")
        ], parsed);
    }

    [Theory]
    [InlineData("poem")]
    [InlineData("")]
    [InlineData("<script>")]
    [InlineData("centre")]
    public void An_unknown_kind_reads_as_text(string kind)
    {
        Assert.Equal([Text("نص")], Parse($"<p data-kind=\"{WebUtility.HtmlEncode(kind)}\">نص</p>"));
    }

    [Fact]
    public void A_kind_is_read_in_any_case_with_spaces_around()
    {
        Assert.Equal([(ParagraphKinds.Center, "نص", null)], Parse("<p data-kind=\" CENTER \">نص</p>"));
        Assert.Equal([(ParagraphKinds.Quote, "نص", null)], Parse("<P DATA-KIND=\"Quote\">نص</P>"));
    }

    [Fact]
    public void Every_other_attribute_on_a_paragraph_is_dropped()
    {
        var parsed = ChapterFormat.Parse(
            "<p class=\"min-h-[1em]\" style=\"text-align:center\" dir=\"rtl\" id=\"x\" onclick=\"alert(1)\" data-kind=\"center\">نص</p>");

        Assert.Equal([new FormattedParagraph(ParagraphKinds.Center, "نص", null)], parsed);
    }

    // ---- Scene breaks ----

    [Theory]
    [InlineData("<hr>")]
    [InlineData("<hr/>")]
    [InlineData("<hr class=\"divider\" style=\"x\">")]
    [InlineData("<p data-kind=\"break\"></p>")]
    [InlineData("<p data-kind=\"break\">* * *</p>")]
    [InlineData("<p data-kind=\"break\">نص لا يُحفظ</p>")]
    public void A_scene_break_stores_three_stars_whatever_it_held(string wire)
    {
        Assert.Equal([Break], Parse(wire));
    }

    [Fact]
    public void A_scene_break_inside_a_paragraph_splits_it()
    {
        Assert.Equal([Text("قبل"), Break, Text("بعد")], Parse("<p>قبل<hr>بعد</p>"));
        Assert.Equal([Text("أ"), Break, Break, Text("ب")], Parse("أ<hr><hr>ب"));
    }

    // ---- Inline formatting ----

    [Fact]
    public void Bold_and_italic_are_normalized_to_strong_and_em()
    {
        Assert.Equal([Text("<strong>عريض</strong> و<em>مائل</em>")], Parse("<p><b>عريض</b> و<i>مائل</i></p>"));
    }

    [Theory]
    [InlineData("<p><strong>أ</strong><strong>ب</strong></p>", "<strong>أب</strong>")]
    [InlineData("<p><b><strong>أ</strong></b></p>", "<strong>أ</strong>")]
    [InlineData("<p><em><strong>أ</strong></em></p>", "<strong><em>أ</em></strong>")]
    [InlineData("<p><u><s><i><b>أ</b></i></s></u></p>", "<strong><em><u><s>أ</s></u></em></strong>")]
    [InlineData("<p><em>أ<strong>ب</strong></em></p>", "<em>أ<strong>ب</strong></em>")]
    [InlineData("<p><strong>أ<br>ب</strong></p>", "<strong>أ<br>ب</strong>")]
    [InlineData("<p><strong>أ</strong><br>ب</p>", "<strong>أ</strong><br>ب")]
    [InlineData("<p><strong></strong>أ<em> </em></p>", "أ")]
    [InlineData("<p><b><i>أ</b>ب</i></p>", "<strong><em>أ</em></strong><em>ب</em>")]
    public void Inline_formatting_is_written_one_canonical_way(string wire, string stored)
    {
        Assert.Equal([Text(stored)], Parse(wire));
    }

    [Fact]
    public void Inline_tags_carry_no_attributes()
    {
        Assert.Equal([Text("<strong>أ</strong><em>ب</em><u>ج</u><s>د</s>")], Parse(
            "<p><strong class=\"c\" style=\"color:red\">أ</strong><em onmouseover=\"x()\">ب</em>" +
            "<u title=\"t\">ج</u><s data-x=\"1\">د</s></p>"));
    }

    [Theory]
    [InlineData("<p><span style=\"color:red\">ملون</span> <font face=\"x\" color=\"blue\">خط</font></p>", "ملون خط")]
    [InlineData("<p><a href=\"https://x.test\">رابط</a> و<a href=\"javascript:alert(1)\">آخر</a></p>", "رابط وآخر")]
    [InlineData("<p><sup>1</sup><sub>2</sub><small>3</small><mark>4</mark><code>5</code><del>6</del><ins>7</ins></p>", "1234567")]
    [InlineData("<p><custom-tag>نص</custom-tag></p>", "نص")]
    public void Every_other_tag_is_unwrapped_keeping_its_text(string wire, string stored)
    {
        Assert.Equal([Text(stored)], Parse(wire));
    }

    [Fact]
    public void Blocks_other_than_paragraphs_become_paragraphs_of_their_text()
    {
        Assert.Equal(
            [Text("عنوان"), Text("قسم"), Text("بند أول"), Text("بند ثانٍ"), Text("خلية"), Text("اقتباس")],
            Parse("<h2>عنوان</h2><div>قسم</div><ul><li>بند أول</li><li>بند ثانٍ</li></ul>" +
                  "<table><tr><td>خلية</td></tr></table><blockquote><p>اقتباس</p></blockquote>"));
    }

    // ---- What is removed ----

    [Theory]
    [InlineData("<p>أ<script>alert(1)</script>ب</p>", "أب")]
    [InlineData("<p>أ<style>p { color: red }</style>ب</p>", "أب")]
    [InlineData("<p>أ<iframe src=\"https://x.test\">إطار</iframe>ب</p>", "أب")]
    [InlineData("<p>أ<object data=\"x\"><p>كائن</p></object>ب</p>", "أب")]
    [InlineData("<p>أ<svg><text>رسم</text><script>alert(1)</script></svg>ب</p>", "أب")]
    [InlineData("<p>أ<math><mi>x</mi></math>ب</p>", "أب")]
    [InlineData("<p>أ<noscript><b>بديل</b></noscript>ب</p>", "أب")]
    [InlineData("<p>أ<template><p>قالب</p></template>ب</p>", "أب")]
    [InlineData("<p>أ<textarea>حقل</textarea><select><option>خيار</option></select><button>زر</button>ب</p>", "أب")]
    [InlineData("<p>أ<video src=\"x\">فيديو</video><audio>صوت</audio><canvas>رسم</canvas>ب</p>", "أب")]
    [InlineData("<p>أ<!-- تعليق -->ب</p>", "أب")]
    [InlineData("<p>أ<embed src=\"x\"><input value=\"قيمة\">ب</p>", "أب")]
    public void Elements_whose_content_is_not_text_go_with_their_content(string wire, string stored)
    {
        Assert.Equal([Text(stored)], Parse(wire));
    }

    [Theory]
    [InlineData("<p>نص<img src=\"x\" onerror=\"alert(1)\"></p>")]
    [InlineData("<p>نص<img src=\"javascript:alert(1)\"></p>")]
    [InlineData("<p>نص<img src=\"data:image/png;base64,iVBORw0KGgo=\"></p>")]
    [InlineData("<p>نص<img src=\"/uploads/a.png\"></p>")]
    [InlineData("<p>نص<img src=\"ftp://files.test/a.png\"></p>")]
    [InlineData("<p>نص<img></p>")]
    public void A_picture_without_an_http_address_is_dropped(string wire)
    {
        Assert.Equal([Text("نص")], Parse(wire));
    }

    [Fact]
    public void Nothing_but_the_format_survives_markup_that_runs_or_loads()
    {
        string[] inputs =
        [
            "<p onclick=\"alert(1)\">أ<img src=x onerror=alert(1)>ب</p>",
            "<p><a href=\"javascript:alert(document.cookie)\">رابط</a></p>",
            "<p><svg onload=alert(1)><circle></circle></svg>نص</p>",
            "<p><iframe srcdoc=\"<script>alert(1)</script>\"></iframe>نص</p>",
            "<p><style>@import 'x'</style><link rel=stylesheet href=x>نص</p>",
            "<p><math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
            "<p><noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
            "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>",
            "<scr<script>ipt>alert(1)</script>",
            "<p><b onmouseover=alert(1)>عريض</b><form action=x><input></form></p>",
            "<<p>>نص<</p>>",
            "<p><img src=\"https://x.test/a.png\" onerror=\"alert(1)\" style=\"x\"></p>"
        ];

        foreach (var paragraph in inputs.SelectMany(ChapterFormat.Parse))
        {
            if (paragraph.Kind == ParagraphKinds.Image)
            {
                Assert.Equal("https://x.test/a.png", paragraph.Content);
                continue;
            }

            Assert.Matches(InlineV1(), paragraph.Content);
            Assert.DoesNotContain("<script", paragraph.Content, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- Text and character references ----

    [Theory]
    [InlineData("<p>&lt;b&gt;ليس وسماً&lt;/b&gt;</p>", "&lt;b&gt;ليس وسماً&lt;/b&gt;")]
    [InlineData("<p>س &lt; ص و ص &gt; ع &amp; ق</p>", "س &lt; ص و ص &gt; ع &amp; ق")]
    [InlineData("س < ص و ص > ع & ق", "س &lt; ص و ص &gt; ع &amp; ق")]
    [InlineData("<p>قال &quot;نعم&quot; و&#39;لا&#39;</p>", "قال \"نعم\" و'لا'")]
    [InlineData("<p>&#1603;&#x062A;&#1576;</p>", "كتب")]
    [InlineData("<p>كان&nbsp;&nbsp;هنا&nbsp;</p>", "كان&nbsp;&nbsp;هنا&nbsp;")]
    [InlineData("<p>&amp;lt; مرمَّزة مرتين</p>", "&amp;lt; مرمَّزة مرتين")]
    [InlineData("<p>&foo; غير معروف</p>", "&amp;foo; غير معروف")]
    public void Text_is_stored_html_escaped(string wire, string stored)
    {
        Assert.Equal([Text(stored)], Parse(wire));
    }

    [Fact]
    public void Every_letter_diacritic_and_mark_is_kept_as_written()
    {
        const string text = "كَتَبَ الكاتبُ «رسالةً» — ـــ ١٢٣ 123 ‏؟! 😀 ‌";
        Assert.Equal([Text(text.TrimEnd())], Parse($"<p>{text}</p>"));
    }

    // ---- Plain text, line breaks and spaces ----

    [Fact]
    public void Plain_text_with_blank_lines_is_text_paragraphs_as_before()
    {
        Assert.Equal([Text("أول"), Text("ثانٍ"), Text("ثالث")], Parse("أول\n\nثانٍ\r\n\r\n\n\nثالث\n\n  "));
        Assert.Equal([Text("أول"), Text("ثانٍ")], Parse("  أول  \n \t \n  ثانٍ"));
    }

    [Fact]
    public void A_single_line_break_in_the_text_is_a_line_break_in_the_paragraph()
    {
        Assert.Equal([Text("سطر أول<br>سطر ثانٍ"), Text("فقرة")], Parse("سطر أول\nسطر ثانٍ\n\nفقرة"));
        Assert.Equal([Text("سطر<br>سطر")], Parse("<p>سطر\r\nسطر</p>"));
    }

    [Fact]
    public void Text_outside_paragraphs_is_a_paragraph_of_its_own()
    {
        Assert.Equal([Text("مقدمة"), Text("فقرة"), Text("خاتمة")], Parse("مقدمة<p>فقرة</p>خاتمة"));
    }

    [Theory]
    [InlineData("<p>  كان   ياما \t كان  </p>", "كان ياما كان")]
    [InlineData("<p> <br> سطر <br> سطر <br> </p>", "سطر<br>سطر")]
    [InlineData("<p>سطر<br><br><br>بعد فراغ</p>", "سطر<br><br><br>بعد فراغ")]
    [InlineData("<p><strong>كان </strong> <em> ياما</em></p>", "<strong>كان </strong><em>ياما</em>")]
    [InlineData("<p>&nbsp;&nbsp;مسافة بادئة</p>", "&nbsp;&nbsp;مسافة بادئة")]
    public void Spaces_a_reader_cannot_see_are_dropped(string wire, string stored)
    {
        Assert.Equal([Text(stored)], Parse(wire));
    }

    [Theory]
    [InlineData("<p></p>")]
    [InlineData("<p> </p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<p class=\"min-h-[1em]\"></p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<p><strong> </strong><em></em></p>")]
    [InlineData("<p data-kind=\"center\"><br><br></p>")]
    [InlineData("<p data-kind=\"quote\"></p>")]
    [InlineData("<p data-kind=\"image\"></p>")]
    [InlineData("<div><span> </span></div>")]
    [InlineData("\n\n  \n")]
    [InlineData("")]
    [InlineData("<script>alert(1)</script>")]
    public void Empty_paragraphs_are_dropped(string wire)
    {
        Assert.Empty(Parse(wire));
    }

    // ---- The web editor's output ----

    /// <summary>What the web editor (Tiptap) sends today: every paragraph with its class, line breaks, empty paragraphs.</summary>
    private const string WebEditorOutput =
        "<p class=\"min-h-[1em]\">في صباح اليوم التالي، وصل فادي.<br>كان وجه ألكسندر خالياً من التعابير.</p>" +
        "<p class=\"min-h-[1em]\"></p>" +
        "<p class=\"min-h-[1em]\"><strong>«حسناً»</strong> قال، ثم <em>صمت</em> طويلاً &amp; <u>ابتسم</u> <s>قليلاً</s>.</p>" +
        "<p class=\"min-h-[1em]\">مكتوب عليه: &lt;&lt;اليوم: 13_9&gt;&gt;&nbsp;</p>";

    [Fact]
    public void The_web_editors_output_keeps_its_formatting_and_loses_only_the_class_and_empty_paragraphs()
    {
        Assert.Equal(
        [
            Text("في صباح اليوم التالي، وصل فادي.<br>كان وجه ألكسندر خالياً من التعابير."),
            Text("<strong>«حسناً»</strong> قال، ثم <em>صمت</em> طويلاً &amp; <u>ابتسم</u> <s>قليلاً</s>."),
            Text("مكتوب عليه: &lt;&lt;اليوم: 13_9&gt;&gt;&nbsp;")
        ], Parse(WebEditorOutput));
    }

    [Fact]
    public void The_web_editors_output_round_trips()
    {
        var once = ChapterFormat.Parse(WebEditorOutput);
        var twice = ChapterFormat.Parse(Wire(once));

        Assert.Equal(once, twice);
        Assert.Equal(
            once.Select(p => p.Content),
            ChapterFormat.Parse(string.Concat(once.Select(p => $"<p class=\"min-h-[1em]\">{p.Content}</p>"))).Select(p => p.Content));
    }

    // ---- Pictures ----

    [Theory]
    [InlineData("<img src=\"https://files.test/a.png\">")]
    [InlineData("<p><img src=\"https://files.test/a.png\"></p>")]
    [InlineData("<p class=\"min-h-[1em]\"> <img src=\"https://files.test/a.png\" alt=\"وصف\"> <br></p>")]
    [InlineData("<p data-kind=\"center\"><strong><img src=\"https://files.test/a.png\"></strong></p>")]
    [InlineData("<p data-kind=\"image\"><img src=\"https://files.test/a.png\"></p>")]
    public void A_picture_alone_is_an_image_paragraph(string wire)
    {
        Assert.Equal([(ParagraphKinds.Image, "https://files.test/a.png", null)], Parse(wire));
    }

    [Fact]
    public void A_picture_among_text_is_split_out_and_the_text_around_it_kept()
    {
        Assert.Equal(
            [Text("قبل الصورة"), (ParagraphKinds.Image, "https://files.test/a.png", null), Text("<strong>بعدها</strong>")],
            Parse("<p>قبل الصورة <img src=\"https://files.test/a.png\"> <strong>بعدها</strong></p>"));
        Assert.Equal(
            [(ParagraphKinds.Center, "بيت", null), (ParagraphKinds.Image, "https://files.test/a.png", null),
             (ParagraphKinds.Image, "https://files.test/b.png", null), (ParagraphKinds.Center, "بيت آخر", null)],
            Parse("<p data-kind=\"center\">بيت<img src=\"https://files.test/a.png\"><img src=\"https://files.test/b.png\">بيت آخر</p>"));
    }

    [Fact]
    public void An_image_paragraphs_text_is_its_caption_stored_as_plain_text()
    {
        Assert.Equal(
            [(ParagraphKinds.Image, "https://files.test/a.png", "خريطة <المدينة> القديمة & أسوارها")],
            Parse("<p data-kind=\"image\"> <strong>خريطة</strong> &lt;المدينة&gt;<br>القديمة &amp; أسوارها <img src=\"https://files.test/a.png\"></p>"));
    }

    [Fact]
    public void An_image_paragraph_without_one_picture_is_text()
    {
        Assert.Equal([Text("لا صورة هنا")], Parse("<p data-kind=\"image\">لا صورة هنا</p>"));
        Assert.Equal([Text("صورة"), (ParagraphKinds.Image, "https://files.test/a.png", null), Text("وأخرى"),
                      (ParagraphKinds.Image, "https://files.test/b.png", null)],
            Parse("<p data-kind=\"image\">صورة<img src=\"https://files.test/a.png\">وأخرى<img src=\"https://files.test/b.png\"></p>"));
        Assert.Equal([Text("تعليق")], Parse("<p data-kind=\"image\"><img src=\"javascript:alert(1)\">تعليق</p>"));
    }

    [Theory]
    [InlineData("http://files.test/a.png", "http://files.test/a.png")]
    [InlineData(" HTTPS://Files.Test/a.png ", "https://files.test/a.png")]
    [InlineData("https://files.test/صورة الغلاف.png", "https://files.test/%D8%B5%D9%88%D8%B1%D8%A9%20%D8%A7%D9%84%D8%BA%D9%84%D8%A7%D9%81.png")]
    [InlineData("https://files.test/a.png?x=1&y=\"2\"<3>", "https://files.test/a.png?x=1&y=%222%22%3C3%3E")]
    [InlineData("https://files.test", "https://files.test/")]
    public void A_pictures_address_is_kept_normalized(string src, string stored)
    {
        Assert.Equal([(ParagraphKinds.Image, stored, null)], Parse($"<img src=\"{WebUtility.HtmlEncode(src)}\">"));
    }

    // ---- Canonical: cleaning again changes nothing ----

    public static TheoryData<string> Inputs() =>
    [
        WebEditorOutput,
        "أول\n\nثانٍ\nسطر",
        "<p><b>أ</b> <i>ب</i> <u> ج </u><s>د</s><br>هـ&nbsp;&nbsp;و</p><hr><p data-kind=\"quote\"><em>رسالة</em><br>توقيع</p>",
        "<p data-kind=\"center\">بيت<img src=\"https://files.test/a.png\">بيت آخر</p>",
        "<p data-kind=\"image\"><img src=\"https://files.test/a b.png\"><strong>تعليق</strong> طويل</p>",
        "<div><h1>عنوان</h1><span>نص <a href=\"x\">مع رابط</a></span></div><script>x</script>",
        "<p><strong>أ<em>ب<u>ج<s>د</s></u></em></strong> <s><u><em><strong>هـ</strong></em></u></s></p>",
        "س < ص & ع > ق \"اقتباس\" 'مفرد'",
        "<p>a<b>b<i>c</b>d</i>e</p>",
        "<p>  <br>  <strong> <br> مسافات </strong>  <br>  </p>"
    ];

    [Theory]
    [MemberData(nameof(Inputs))]
    public void Cleaning_its_own_output_again_changes_nothing(string input)
    {
        var once = ChapterFormat.Parse(input);
        Assert.NotEmpty(once);

        Assert.Equal(once, ChapterFormat.Parse(Wire(once)));
        foreach (var paragraph in once)
        {
            // A stored paragraph is read back as it was stored.
            Assert.Equal(paragraph, ChapterFormat.Read(paragraph.Content, paragraph.Kind, paragraph.Caption));
            if (ParagraphKinds.HoldsText(paragraph.Kind))
            {
                Assert.Matches(InlineV1(), paragraph.Content);
            }
        }
    }

    [Fact]
    public void Cleaning_keeps_every_word_of_a_real_chapter()
    {
        foreach (var stored in Integration.ProductionChapter.Paragraphs)
        {
            var read = ChapterFormat.Read(stored, ParagraphKinds.Text, null);

            Assert.Equal(ParagraphKinds.Text, read.Kind);
            Assert.Equal(ParagraphText.VisibleText(stored), read.VisibleText);
            Assert.DoesNotContain("class", read.Content);
        }
    }

    // ---- Stored paragraphs: what the API serves, and what the maintenance stores ----

    [Fact]
    public void A_stored_paragraph_from_before_the_format_is_served_cleaned()
    {
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "نص<br>سطر", null),
            ChapterFormat.Read("<p class=\"min-h-[1em]\">نص<br>سطر", ParagraphKinds.Text, null));
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "أب", null),
            ChapterFormat.Read("<p onclick=\"alert(1)\">أ<script>alert(1)</script>ب", ParagraphKinds.Text, null));
        // Several blocks in one stored row are one paragraph with line breaks.
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Quote, "أ<br>ب", null),
            ChapterFormat.Read("<div>أ</div><div>ب</div>", ParagraphKinds.Quote, null));
        // An unknown stored kind is text.
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "نص", null), ChapterFormat.Read("نص", "poem", null));
    }

    [Fact]
    public void A_stored_paragraph_is_served_as_one_paragraph_whatever_it_converts_to()
    {
        // A picture among text: the text (the maintenance splits the picture out into a paragraph of its own).
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "قبل<br>بعد", null),
            ChapterFormat.Read("قبل <img src=\"https://files.test/a.png\"> بعد", ParagraphKinds.Text, null));
        // A picture alone: the picture.
        Assert.Equal(FormattedParagraph.Picture("https://files.test/a.png", null),
            ChapterFormat.Read("<p class=\"min-h-[1em]\"><img src=\"https://files.test/a.png\">", ParagraphKinds.Text, null));
        // Nothing left: an empty paragraph of its kind.
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Center, "", null), ChapterFormat.Read("<p><br></p>", ParagraphKinds.Center, null));
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "", null), ChapterFormat.Read("<script>x</script>", ParagraphKinds.Text, null));
    }

    [Fact]
    public void Stored_breaks_and_pictures_are_served_by_their_kind()
    {
        Assert.Equal(FormattedParagraph.SceneBreak, ChapterFormat.Read("<p>نص</p>", ParagraphKinds.Break, null));
        Assert.Equal(FormattedParagraph.Picture("https://files.test/a.png", "تعليق"),
            ChapterFormat.Read("https://files.test/a.png", ParagraphKinds.Image, "  تعليق \n "));
        Assert.Equal(FormattedParagraph.Picture("https://files.test/a.png", null),
            ChapterFormat.Read("https://files.test/a.png", "IMAGE", " "));
        Assert.Equal(new FormattedParagraph(ParagraphKinds.Text, "", null),
            ChapterFormat.Read("javascript:alert(1)", ParagraphKinds.Image, null));
    }

    [Fact]
    public void Converting_a_stored_paragraph_counts_its_pictures()
    {
        var split = ChapterFormat.Convert("قبل<img src=\"https://files.test/a.png\">بعد<img src=\"data:x\">", ParagraphKinds.Text, null);
        Assert.Equal([new FormattedParagraph(ParagraphKinds.Text, "قبل", null), FormattedParagraph.Picture("https://files.test/a.png", null),
                      new FormattedParagraph(ParagraphKinds.Text, "بعد", null)], split.Paragraphs);
        Assert.Equal((2, 1), (split.Pictures, split.PicturesKept));

        Assert.Equal((1, 1), Pictures(ChapterFormat.Convert("https://files.test/a.png", ParagraphKinds.Image, null)));
        Assert.Equal((1, 0), Pictures(ChapterFormat.Convert("data:image/png;base64,AAAA", ParagraphKinds.Image, null)));
        Assert.Empty(ChapterFormat.Convert("data:image/png;base64,AAAA", ParagraphKinds.Image, null).Paragraphs);
        Assert.Equal((0, 0), Pictures(ChapterFormat.Convert("<p class=\"min-h-[1em]\">نص", ParagraphKinds.Text, null)));

        static (int, int) Pictures(StoredParagraphConversion conversion) => (conversion.Pictures, conversion.PicturesKept);
    }

    // ---- The chapter's limit ----

    [Fact]
    public void The_length_limit_counts_only_what_readers_see()
    {
        var paragraphs = ChapterFormat.Parse(
            "<p class=\"min-h-[1em]\"><strong>ثلاث</strong>&nbsp; <em>كلمات</em>   هنا</p><hr>" +
            "<p data-kind=\"image\"><img src=\"https://files.test/a.png\">تعليق</p>");

        // "ثلاث كلمات هنا" (14) and the caption (5); the break and the markup count nothing.
        Assert.Equal(19, ChapterFormat.VisibleLength(paragraphs));
    }

    /// <summary>Paragraphs back on the wire, as an editor that loaded them would send them.</summary>
    internal static string Wire(IEnumerable<FormattedParagraph> paragraphs) => string.Concat(paragraphs.Select(p => p.Kind switch
    {
        ParagraphKinds.Break => "<hr>",
        ParagraphKinds.Image => $"<p data-kind=\"image\"><img src=\"{WebUtility.HtmlEncode(p.Content)}\">{WebUtility.HtmlEncode(p.Caption ?? "")}</p>",
        _ => $"<p data-kind=\"{p.Kind}\">{p.Content}</p>"
    }));
}
