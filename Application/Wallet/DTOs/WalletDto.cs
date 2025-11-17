namespace Application.Wallet.DTOs;

public class WalletDto
{
    public decimal CurrentBalance { get; set; }
    public decimal TotalRecharged { get; set; }
    public decimal TotalWithdrawn { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal TotalEarned { get; set; }
}
