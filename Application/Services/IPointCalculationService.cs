namespace Application.Services;

public interface IPointCalculationService
{
    (decimal basePrice, decimal fee, decimal total) CalculateRechargeTotal(int points);
    (decimal baseAmount, decimal tax, decimal netAmount) CalculateWithdrawalNet(int points);
    decimal ConvertToUSD(decimal egp);
    decimal ConvertToEGP(decimal usd);
}
