using System.Globalization;

namespace Application.Common;

/// <summary>Point amounts in messages: "500" rather than the column's "500.00", with Latin digits whatever the host culture.</summary>
public static class Points
{
    public static string Format(decimal points) => points.ToString("0.##", CultureInfo.InvariantCulture);
}
