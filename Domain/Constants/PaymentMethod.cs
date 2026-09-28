namespace Domain.Constants;

public static class PaymentMethod
{
    public const string VodafoneCash = "VodafoneCash";
    public const string InstaPay = "InstaPay";
    public const string PayPal = "PayPal";

    /// <summary>The method's name in Arabic, as the web shows it; an unknown one as it is.</summary>
    public static string ArabicName(string method) => method switch
    {
        VodafoneCash => "فودافون كاش",
        InstaPay => "إنستاباي",
        PayPal => "باي بال",
        _ => method
    };
}
