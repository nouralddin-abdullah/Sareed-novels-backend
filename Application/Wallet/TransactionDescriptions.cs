using System.Globalization;
using Domain.Constants;

namespace Application.Wallet;

/// <summary>
/// The Arabic descriptions of wallet entries (PointTransaction.Description). Clients can show them as they are, or
/// build their own text from the entry's type, novel and gift. Entries written before #17 were English; the migration
/// ArabicGiftNamesAndTransactionDetails rewrote the ones whose English pattern could be read without guessing.
/// </summary>
public static class TransactionDescriptions
{
    public static string GiftSent(string giftNameAr, int count, string novelTitle) =>
        $"أرسلت {giftNameAr} ×{count} إلى رواية «{novelTitle}»";

    public static string GiftReceived(string giftNameAr, int count, string senderName, string novelTitle) =>
        $"استلمت {giftNameAr} ×{count} من {senderName} على رواية «{novelTitle}»";

    public static string PrivilegeSubscription(string novelTitle) => $"اشتراك دائم في امتيازات رواية «{novelTitle}»";

    public static string PrivilegeRevenue(string novelTitle) => $"عائد اشتراك في امتيازات رواية «{novelTitle}»";

    public static string RechargeApproved(int points, decimal amountEgp, string paymentMethod) =>
        $"شحن رصيد: {points} نقطة ({Egp(amountEgp)} جنيه عبر {PaymentMethod.ArabicName(paymentMethod)})";

    public static string WithdrawalApproved(int points, decimal amountEgp, string paymentMethod) =>
        $"سحب رصيد: {points} نقطة ({Egp(amountEgp)} جنيه عبر {PaymentMethod.ArabicName(paymentMethod)})";

    private static string Egp(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
