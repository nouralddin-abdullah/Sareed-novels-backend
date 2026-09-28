using System.Globalization;
using Domain.Constants;

namespace Application.Wallet;

/// <summary>Arabic messages the recharge and withdrawal request handlers share.</summary>
internal static class RequestMessages
{
    public const string AlreadyProcessed = "عولج هذا الطلب من قبل";

    /// <summary>A request an admin already decided: accepted or refused (the status used to be glued into English).</summary>
    public static string AlreadyDecided(string status) => status switch
    {
        RequestStatus.Approved => "قُبل هذا الطلب من قبل",
        RequestStatus.Rejected => "رُفض هذا الطلب من قبل",
        _ => AlreadyProcessed
    };

    /// <summary>An amount in Egyptian pounds, with Latin digits whatever the host culture: «187.50».</summary>
    public static string Egp(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
