namespace Application.Wallet.DTOs;

public class WithdrawalRequestDto
{
    public Guid Id { get; set; }
    public int PointsRequested { get; set; }
    public decimal BaseAmountEGP { get; set; }
    public decimal TaxDeducted { get; set; }
    public decimal NetAmountEGP { get; set; }
    public string WithdrawalMethod { get; set; } = default!;
    public string PaymentDetails { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? RejectionReason { get; set; }
    
    // For admin view
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }
}
