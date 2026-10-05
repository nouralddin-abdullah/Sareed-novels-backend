namespace Domain.Entities;

public class UserWallet
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    
    public decimal CurrentBalance { get; set; } = 0;

    // The four totals below have never been written by any code (always 0 on rows the API made). The ledger
    // (PointTransactions) is the record: GET /api/wallet's totalEarned comes from it (#78), not from TotalEarned.
    public decimal TotalRecharged { get; set; } = 0;
    public decimal TotalWithdrawn { get; set; } = 0;
    public decimal TotalSpent { get; set; } = 0;
    public decimal TotalEarned { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public void AddPoints(decimal amount)
    {
        CurrentBalance += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DeductPoints(decimal amount)
    {
        if (CurrentBalance < amount)
        {
            throw new InvalidOperationException("Insufficient balance");
        }
        CurrentBalance -= amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool HasSufficientBalance(decimal amount)
    {
        return CurrentBalance >= amount;
    }
}
