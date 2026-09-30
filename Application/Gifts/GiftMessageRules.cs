using Application.Common;
using Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace Application.Gifts;

/// <summary>
/// The short message a sender may write to the author with a gift (#31). It is public: it shows under the novel's
/// recent gifts. Its limit counts user-perceived characters (<see cref="TextElements"/>, as Flutter's counter and the
/// web's Intl.Segmenter count them), so an emoji is one character, not the two or more UTF-16 units it takes.
/// </summary>
public static class GiftMessageRules
{
    /// <summary>
    /// Where the limit is configured. GET /api/app/config serves the same value as gifts.messageMaxLength, read by
    /// <see cref="MaxLength"/> in both places, so the apps' counters and the server can't disagree.
    /// </summary>
    public const string MaxLengthKey = "AppConfig:Gifts:MessageMaxLength";

    public const int DefaultMaxLength = 200;

    /// <summary>The highest limit the configuration may set; a value beyond it is a mistake and fails loudly.</summary>
    public const int HighestMaxLength = 1000;

    public const string TooLongCode = "GiftMessageTooLong";

    /// <summary>Refused because the novel's author blocked the sender (a gift without a message still goes).</summary>
    public const string BlockedCode = "Blocked";

    public const string BlockedMessage = "لا يمكنك إرسال رسالة إلى هذا الكاتب.";

    public static string TooLongMessage(int maxLength) => $"الرسالة طويلة: الحد الأقصى {maxLength} حرف.";

    /// <summary>
    /// The configured limit (<see cref="DefaultMaxLength"/> when not set). A value outside 1 to
    /// <see cref="HighestMaxLength"/>, or not a number, throws: a configuration mistake is an error, not a wrong answer.
    /// </summary>
    public static int MaxLength(IConfiguration configuration)
    {
        var maxLength = configuration.GetValue(MaxLengthKey, DefaultMaxLength);
        return maxLength is >= 1 and <= HighestMaxLength
            ? maxLength
            : throw new InvalidOperationException($"{MaxLengthKey} must be from 1 to {HighestMaxLength}, not {maxLength}");
    }

    /// <summary>The message as it is stored: trimmed, and null when empty or only whitespace.</summary>
    public static string? Normalize(string? message) => string.IsNullOrWhiteSpace(message) ? null : message.Trim();

    /// <summary>
    /// Whether a (normalized) message is over the limit: more than <paramref name="maxLength"/> text elements, or more
    /// UTF-16 units than its column holds (<see cref="GiftTransaction.MessageMaxStoredLength"/>).
    /// </summary>
    public static bool IsTooLong(string message, int maxLength) =>
        message.Length > GiftTransaction.MessageMaxStoredLength || TextElements.Count(message) > maxLength;

    /// <summary>What a sender's message becomes: <see cref="Normalize"/>d, and whether it is too long to be sent.</summary>
    public static GiftMessageCheck Check(string? message, int maxLength)
    {
        var normalized = Normalize(message);
        return new GiftMessageCheck(normalized, normalized is not null && IsTooLong(normalized, maxLength));
    }
}

/// <param name="Message">The message to store; null when there is none.</param>
/// <param name="TooLong">True when it must be refused (400 GiftMessageTooLong, nothing charged).</param>
public readonly record struct GiftMessageCheck(string? Message, bool TooLong);
