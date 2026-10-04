using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Domain.Repositories;
using Google.Apis.Auth;

namespace Application.Users;

/// <summary>
/// The user name (the public handle, /profile/{userName}) of an account made by signing in with Google (#69): POST
/// /api/identity/google-login, and the web's /google-callback, which ends in the same command. It never comes from the
/// email address. In order:
/// <list type="number">
/// <item>From the person's Google name when every letter of it is Latin (<see cref="FromName"/>): «Shahd Elattar» is
/// shahd-elattar.</item>
/// <item>When another account holds that, the same with a number: shahd-elattar-2, -3 ... up to
/// -<see cref="NumberedUpTo"/> (<see cref="Numbered"/>), all looked up in one query.</item>
/// <item>Otherwise, and when all of those are taken: "sarduser" and six digits, as before #69 (<see cref="Fallback"/>).</item>
/// </list>
/// Every handle meets update-me's rules (<see cref="UserNameCheck"/>), so the member can keep it through any later edit
/// of their profile. The name's own handle must meet them as it is (a two-letter name gives none); the number only
/// makes room for a namesake.
/// </summary>
public sealed class GoogleUserNames(UserNameCheck userNameCheck, IUsersRepository users)
{
    /// <summary>
    /// The highest number tried after the name's own handle: name, name-2 ... name-20, twenty handles looked up in one
    /// query. Past that the account gets a "sarduser" handle, and the app asks the member to choose one.
    /// </summary>
    public const int NumberedUpTo = 20;

    /// <summary>How many "sarduser" handles a new account tries after those (each taken one draws the next).</summary>
    public const int FallbackAttempts = 5;

    /// <summary>
    /// The user names a new account tries, in order: the handles from the name that no account holds now, then the
    /// "sarduser" ones. The account is created with the first one still free when it is saved: a sign-in at the same
    /// moment may take one first.
    /// </summary>
    public async Task<IReadOnlyList<string>> CandidatesAsync(GoogleJsonWebSignature.Payload payload, CancellationToken cancellationToken)
    {
        var fromName = await FreeFromNameAsync(NameOf(payload), cancellationToken);
        return [.. fromName, .. Enumerable.Range(0, FallbackAttempts).Select(attempt => Fallback(payload.Subject, attempt))];
    }

    /// <summary>The name a handle comes from: the person's full name at Google, else their given and family names.</summary>
    private static string NameOf(GoogleJsonWebSignature.Payload payload) =>
        string.IsNullOrWhiteSpace(payload.Name) ? $"{payload.GivenName} {payload.FamilyName}" : payload.Name;

    private async Task<IReadOnlyList<string>> FreeFromNameAsync(string name, CancellationToken cancellationToken)
    {
        var handle = FromName(name);
        if (handle is null || !userNameCheck.AllowsForNewAccount(handle))
        {
            return [];
        }

        var candidates = Numbered(handle).Where(userNameCheck.AllowsForNewAccount).ToList();
        var taken = await users.GetTakenUserNamesAsync(candidates, cancellationToken);
        return candidates.Where(candidate => !taken.Contains(candidate)).ToList();
    }

    /// <summary>
    /// The handle a name gives before anything is looked up, or null when it gives none: a letter that isn't Latin
    /// (Arabic, or any other script: nothing is transliterated), or no Latin letter at all. Each letter is written in
    /// a-z: lower case, without its accents (é is e, and Latin letters with none to strip as they are written without
    /// one: ß is ss, ø is o, ł is l), and styled letters as plain ones (𝓢, Ｓ). Digits stay in the words. The words,
    /// split by spaces, "-", "_", ".", and dashes, are joined with "-"; every other character is dropped (apostrophes,
    /// brackets and other punctuation, symbols, emoji). Over <see cref="UserNameRules.MaxLength"/> characters the
    /// handle is cut at the last word that fits, or in the middle of the first word when cutting there would leave
    /// fewer than <see cref="UserNameRules.MinLength"/> characters.
    /// </summary>
    public static string? FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || rune.Value is '_' or '.' || category == UnicodeCategory.DashPunctuation)
            {
                EndWord();
            }
            else if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                     or UnicodeCategory.TitlecaseLetter or UnicodeCategory.OtherLetter)
            {
                if (!TryAppendLatin(rune, word))
                {
                    return null;
                }
            }
            else if (category == UnicodeCategory.DecimalDigitNumber)
            {
                AppendDigit(rune, word);
            }
            // Anything else is dropped: accents written on their own, modifier letters (ʻ in Hawaiʻi), apostrophes and
            // other punctuation, symbols, emoji and the characters that join them.
        }
        EndWord();

        return words.Any(w => w.Any(char.IsAsciiLetterLower)) ? Fit(string.Join('-', words), UserNameRules.MaxLength) : null;

        void EndWord()
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }
    }

    /// <summary>
    /// The handles tried for a name's <paramref name="handle"/>, in order: itself, then handle-2 to
    /// handle-<see cref="NumberedUpTo"/>. Where the number would make it longer than
    /// <see cref="UserNameRules.MaxLength"/> characters, the handle gives up letters at its end to make room: the
    /// second of mohamed-abdelrahman (19 characters) is mohamed-abdelrahma-2.
    /// </summary>
    public static IEnumerable<string> Numbered(string handle)
    {
        yield return handle;
        for (var number = 2; number <= NumberedUpTo; number++)
        {
            var suffix = "-" + number.ToString(CultureInfo.InvariantCulture);
            var room = UserNameRules.MaxLength - suffix.Length;
            yield return (handle.Length <= room ? handle : handle[..room].TrimEnd('-')) + suffix;
        }
    }

    /// <summary>
    /// The "sarduser" handle a new account tries on its <paramref name="attempt"/>th try (from 0): six digits from a
    /// hash of Google's id for the person, so each try differs and a test can predict them.
    /// </summary>
    public static string Fallback(string googleSubject, int attempt)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{googleSubject}:{attempt}"));
        return $"sarduser{BinaryPrimitives.ReadUInt32BigEndian(hash) % 900_000 + 100_000}";
    }

    /// <summary>Latin letters that have no accent to strip (Unicode doesn't decompose them), as written without one.</summary>
    private static readonly Dictionary<char, string> Unaccented = new()
    {
        ['ß'] = "ss", ['æ'] = "ae", ['œ'] = "oe", ['ø'] = "o", ['ł'] = "l", ['đ'] = "d", ['ð'] = "d", ['þ'] = "th",
        ['ħ'] = "h", ['ı'] = "i", ['ŧ'] = "t"
    };

    /// <summary>
    /// Appends a letter in a-z: its compatibility decomposition (é is e and an accent, 𝓢 and Ｓ are S) without the
    /// accents and what else it adds (the dot of ŀ), in lower case. False for a letter that isn't Latin.
    /// </summary>
    private static bool TryAppendLatin(Rune letter, StringBuilder word)
    {
        foreach (var part in letter.ToString().Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            if (!Rune.IsLetter(part) || Rune.GetUnicodeCategory(part) == UnicodeCategory.ModifierLetter)
            {
                continue;
            }

            var lower = Rune.ToLowerInvariant(part);
            if (lower.IsAscii && char.IsAsciiLetterLower((char)lower.Value))
            {
                word.Append((char)lower.Value);
            }
            else if (lower.IsBmp && Unaccented.TryGetValue((char)lower.Value, out var plain))
            {
                word.Append(plain);
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Appends a digit as 0-9 (３ and 𝟑 are 3); the digits of other scripts are dropped.</summary>
    private static void AppendDigit(Rune digit, StringBuilder word)
    {
        var plain = digit.ToString().Normalize(NormalizationForm.FormKD);
        if (plain.Length == 1 && char.IsAsciiDigit(plain[0]))
        {
            word.Append(plain[0]);
        }
    }

    /// <summary>
    /// <paramref name="handle"/> in at most <paramref name="maxLength"/> characters: cut at the last word boundary
    /// that fits, unless that leaves fewer than <see cref="UserNameRules.MinLength"/> characters (jo-wolfeschlegelstein
    /// would be jo); then cut at <paramref name="maxLength"/>.
    /// </summary>
    private static string Fit(string handle, int maxLength)
    {
        if (handle.Length <= maxLength)
        {
            return handle;
        }

        var boundary = handle.LastIndexOf('-', maxLength);
        return boundary >= UserNameRules.MinLength ? handle[..boundary] : handle[..maxLength].TrimEnd('-');
    }
}
