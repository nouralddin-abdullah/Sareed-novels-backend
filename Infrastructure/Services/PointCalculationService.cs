using Application.Services;
using Domain.Constants;

namespace Infrastructure.Services;

public class PointCalculationService : IPointCalculationService
{
    public (decimal basePrice, decimal fee, decimal total) CalculateRechargeTotal(int points)
    {
        var basePrice = points * PointsConstants.PointToEGPRate;
        var fee = basePrice * PointsConstants.TransactionFeePercentage;
        var total = basePrice + fee;
        
        return (basePrice, fee, total);
    }

    public (decimal baseAmount, decimal tax, decimal netAmount) CalculateWithdrawalNet(int points)
    {
        var baseAmount = points * PointsConstants.PointToEGPRate;
        var tax = baseAmount * PointsConstants.WithdrawalTaxPercentage;
        var netAmount = baseAmount - tax;
        
        return (baseAmount, tax, netAmount);
    }

    public decimal ConvertToUSD(decimal egp)
    {
        return Math.Round(egp / PointsConstants.USDToEGPRate, 2);
    }

    public decimal ConvertToEGP(decimal usd)
    {
        return Math.Round(usd * PointsConstants.USDToEGPRate, 2);
    }
}
