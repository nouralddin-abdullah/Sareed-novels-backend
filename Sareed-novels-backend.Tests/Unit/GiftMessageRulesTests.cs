using Application.Gifts;
using Domain.Constants;
using Domain.Entities;
using Infrastructure.Push;
using Microsoft.Extensions.Configuration;

namespace Sareed_novels_backend.Tests.Unit;

/// <summary>
/// The message a sender may write with a gift (#31): trimmed, empty is none, and at most 200 user-perceived characters,
/// counted as Flutter's counter counts them (an emoji is one), so the server never refuses what the app allowed.
/// </summary>
public class GiftMessageRulesTests
{
    private const int Limit = GiftMessageRules.DefaultMaxLength;

    // 😀 is one character in two UTF-16 units; a family (four people joined by ZWJ) is one character in 11 units.
    private const string Smile = "\U0001F600";
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467‍\U0001F466";

    private static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));

    [Theory]
    [InlineData("  شكراً على الفصل  ", "شكراً على الفصل")]
    [InlineData("\n\tرائعة\r\n", "رائعة")]
    [InlineData("سطر أول\nسطر ثانٍ", "سطر أول\nسطر ثانٍ")] // inside, the text stays as written
    public void A_message_is_trimmed(string sent, string stored)
    {
        var check = GiftMessageRules.Check(sent, Limit);

        Assert.Equal(stored, check.Message);
        Assert.False(check.TooLong);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    [InlineData(" 　")] // no-break and ideographic spaces
    public void An_empty_or_blank_message_is_no_message(string? sent)
    {
        var check = GiftMessageRules.Check(sent, Limit);

        Assert.Null(check.Message);
        Assert.False(check.TooLong);
    }

    [Fact]
    public void Two_hundred_characters_are_accepted_and_two_hundred_and_one_are_refused()
    {
        var atLimit = Repeat("ب", 200);

        Assert.False(GiftMessageRules.Check(atLimit, Limit).TooLong);
        Assert.True(GiftMessageRules.Check(atLimit + "ب", Limit).TooLong);
        // Spaces around don't count: they are trimmed first.
        Assert.False(GiftMessageRules.Check("   " + atLimit + "   ", Limit).TooLong);
    }

    [Fact]
    public void An_emoji_counts_as_one_character()
    {
        Assert.Equal(1, GiftMessageRules.Length(Smile));
        Assert.Equal(1, GiftMessageRules.Length(Family));
        Assert.Equal(1, GiftMessageRules.Length("\U0001F44D\U0001F3FD")); // with a skin tone
        Assert.Equal(1, GiftMessageRules.Length("\U0001F1F8\U0001F1E6")); // a flag
        Assert.Equal(1, GiftMessageRules.Length("بَ")); // a letter with its tashkeel

        // 400 and 2200 UTF-16 units, 200 characters: accepted, as the app's counter allowed them.
        Assert.False(GiftMessageRules.Check(Repeat(Smile, 200), Limit).TooLong);
        Assert.False(GiftMessageRules.Check(Repeat(Family, 200), Limit).TooLong);
        Assert.True(GiftMessageRules.Check(Repeat(Smile, 201), Limit).TooLong);
        Assert.True(GiftMessageRules.Check(Repeat(Family, 200) + "!", Limit).TooLong);
    }

    [Fact]
    public void Text_longer_than_its_column_is_refused_even_within_the_character_limit()
    {
        // One letter under thousands of combining marks is one character, but doesn't fit nvarchar(4000).
        var zalgo = "a" + Repeat("́", GiftTransaction.MessageMaxStoredLength);

        Assert.Equal(1, GiftMessageRules.Length(zalgo));
        Assert.True(GiftMessageRules.Check(zalgo, Limit).TooLong);
        Assert.False(GiftMessageRules.Check("a" + Repeat("́", GiftTransaction.MessageMaxStoredLength - 1), Limit).TooLong);
    }

    [Fact]
    public void The_refusal_says_the_limit_in_arabic()
    {
        Assert.Equal("GiftMessageTooLong", GiftMessageRules.TooLongCode);
        Assert.Equal("الرسالة طويلة: الحد الأقصى 200 حرف.", GiftMessageRules.TooLongMessage(200));
        Assert.Equal("Blocked", GiftMessageRules.BlockedCode);
        Assert.Equal("لا يمكنك إرسال رسالة إلى هذا الكاتب.", GiftMessageRules.BlockedMessage);
    }

    private static IConfiguration Configuration(string? maxLength) => new ConfigurationBuilder()
        .AddInMemoryCollection(maxLength is null ? [] : [new(GiftMessageRules.MaxLengthKey, maxLength)])
        .Build();

    [Theory]
    [InlineData(null, 200)]
    [InlineData("150", 150)]
    [InlineData("1", 1)]
    [InlineData("1000", 1000)]
    public void The_limit_is_read_from_configuration(string? configured, int expected)
    {
        var maxLength = GiftMessageRules.MaxLength(Configuration(configured));

        Assert.Equal(expected, maxLength);
        Assert.False(GiftMessageRules.Check(Repeat("ب", maxLength), maxLength).TooLong);
        Assert.True(GiftMessageRules.Check(Repeat("ب", maxLength + 1), maxLength).TooLong);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1001")]
    [InlineData("two hundred")]
    public void A_misconfigured_limit_is_an_error(string configured) =>
        Assert.Throws<InvalidOperationException>(() => GiftMessageRules.MaxLength(Configuration(configured)));

    // ─── The push (#31): the sentence, then «the message» on a new line, cut at about 100 characters ───

    private static Notification Gift(string message = "قارئ أرسل وردة إلى روايتك «ظل»") => new()
    {
        Id = Guid.NewGuid(),
        Type = NotificationType.GiftReceived,
        Message = message,
        GiftTransactionId = Guid.NewGuid()
    };

    [Fact]
    public void A_gift_push_quotes_the_message_on_a_new_line()
    {
        var notification = Gift();

        Assert.Equal("قارئ أرسل وردة إلى روايتك «ظل»\n«شكراً لك 🌹»",
            PushMessages.BodyFor(notification, new PushTarget { GiftMessage = "شكراً لك 🌹" }));
        Assert.Equal(notification.Message, PushMessages.BodyFor(notification, new PushTarget()));
        Assert.Equal(notification.Message, PushMessages.BodyFor(notification, new PushTarget { GiftMessage = "  " }));
        // Only a gift's push has one.
        var follow = new Notification { Type = NotificationType.NewFollower, Message = "قارئ بدأ بمتابعتك" };
        Assert.Equal(follow.Message, PushMessages.BodyFor(follow, new PushTarget { GiftMessage = "رسالة" }));
    }

    [Fact]
    public void A_long_message_is_cut_at_about_100_characters_without_splitting_one()
    {
        var hundred = Repeat("ب", 100);
        Assert.Equal(hundred, PushMessages.Excerpt(hundred));

        var cut = PushMessages.Excerpt(hundred + "ب");
        Assert.Equal(Repeat("ب", 99) + "…", cut);
        Assert.Equal(100, GiftMessageRules.Length(cut));

        // Emoji stay whole, and a hundred of the long ones are cut shorter, to keep the push small.
        var smiles = PushMessages.Excerpt(Repeat(Smile, 150));
        Assert.Equal(Repeat(Smile, 99) + "…", smiles);
        var families = PushMessages.Excerpt(Repeat(Family, 100));
        Assert.EndsWith(Family + "…", families);
        Assert.True(families.Length <= 300);
        Assert.Equal(0, (families.Length - 1) % Family.Length);

        // A cut after a space doesn't leave it before the «…».
        Assert.Equal(Repeat("ب", 98) + "…", PushMessages.Excerpt(Repeat("ب", 98) + " " + Repeat("ت", 10)));
    }
}
