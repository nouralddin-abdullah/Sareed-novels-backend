namespace Domain.Entities;

public class WithdrawalRequest
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public User User { get; set; } = default!;
    
    public int PointsRequested { get; set; }
    public decimal BaseAmountEGP { get; set; }
    public decimal TaxDeducted { get; set; }
    public decimal NetAmountEGP { get; set; }
    
    public string WithdrawalMethod { get; set; } = default!; // VodafoneCash, InstaPay, PayPal
    public string PaymentDetails { get; set; } = default!; // Phone/Email for receiving payment
    
    public string Status { get; set; } = Constants.RequestStatus.Pending;
    
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ProcessedBy { get; set; } // Admin UserId
    public User? ProcessedByUser { get; set; }
    public string? RejectionReason { get; set; }
}
