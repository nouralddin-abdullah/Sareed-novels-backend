using System.Globalization;

namespace Application.Common;

/// <summary>
/// Text measured as people count it: in user-perceived characters (extended grapheme clusters, .NET's text elements),
/// as Flutter's counter and the web's Intl.Segmenter count them. An emoji, a flag or a letter with its tashkeel is one
/// character, though it takes two or more UTF-16 units, which is what <see cref="string.Length"/> counts. The limits the
/// apps show a counter for are counted this way: gift messages (#31) and posts (#43).
/// </summary>
public static class TextElements
{
    public static int Count(string text) => new StringInfo(text).LengthInTextElements;
}
