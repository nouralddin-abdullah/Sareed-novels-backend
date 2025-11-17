namespace Application.Wallet.DTOs;

public class PointTransactionDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
}
