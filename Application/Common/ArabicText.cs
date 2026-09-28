namespace Application.Common;

/// <summary>Helpers for the Arabic messages the API sends.</summary>
public static class ArabicText
{
    /// <summary>
    /// Several messages as one text, each a sentence ending with a period, e.g. «تعذّر إنشاء الحساب. اسم المستخدم
    /// مستخدم بالفعل، اختر اسمًا آخر.» (ASP.NET Identity's descriptions after what failed).
    /// </summary>
    public static string Sentences(IEnumerable<string?> parts) =>
        string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim().TrimEnd('.') + "."));
}
