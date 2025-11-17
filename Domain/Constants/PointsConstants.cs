namespace Domain.Constants;

public static class PointsConstants
{
    public const decimal PointToEGPRate = 0.1m; // 10 points = 1 EGP
    public const decimal TransactionFeePercentage = 0.10m; // 10%
    public const decimal WithdrawalTaxPercentage = 0.10m; // 10%
    public const int MinimumRecharge = 500;
    public const int MinimumWithdrawal = 1000;
    public const decimal USDToEGPRate = 47m; // For PayPal conversion
}
