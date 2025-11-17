namespace Application.Wallet.DTOs;

public class RechargeRequestDto
{
    public Guid Id { get; set; }
    public int PointsRequested { get; set; }
    public decimal BaseAmountEGP { get; set; }
    public decimal TransactionFee { get; set; }
    public decimal TotalAmountEGP { get; set; }
    public string PaymentMethod { get; set; } = default!;
    public string? PaymentProofUrl { get; set; }
    public string Status { get; set; } = default!;
    public DateTime RequestedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? RejectionReason { get; set; }
    
    // For admin view
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserEmail { get; set; }
}
