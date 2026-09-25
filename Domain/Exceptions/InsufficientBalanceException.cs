namespace Domain.Exceptions;

/// <summary>A debit was refused because the wallet balance does not cover it.</summary>
public class InsufficientBalanceException(decimal required)
    : InvalidOperationException($"Insufficient balance. Required: {required}")
{
    public decimal Required { get; } = required;
}
