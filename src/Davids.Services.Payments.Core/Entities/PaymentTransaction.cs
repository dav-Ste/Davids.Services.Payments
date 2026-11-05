using Davids.Services.Payments.Core.Exceptions;

namespace Davids.Services.Payments.Core.Entities;

public enum PaymentStatus
{
    Captured = 0,
    Declined = 1
}

public sealed class PaymentTransaction
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid CustomerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "GBP";
    public string PaymentMethod { get; private set; } = "card";
    public string CardLast4 { get; private set; } = "4242";
    public PaymentStatus Status { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PaymentTransaction() { }

    public PaymentTransaction(
        Guid id,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string? currency,
        string? paymentMethod,
        string? cardLast4,
        PaymentStatus status,
        string providerReference,
        string? failureReason,
        DateTime nowUtc)
    {
        if (id == Guid.Empty || orderId == Guid.Empty || customerId == Guid.Empty)
        {
            throw new InvalidPaymentException("Payment, order, and customer identifiers must be non-empty.");
        }

        if (amount <= 0m)
        {
            throw new InvalidPaymentException("Payment amount must be greater than zero.");
        }

        Id = id;
        OrderId = orderId;
        CustomerId = customerId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = string.IsNullOrWhiteSpace(currency) ? "GBP" : currency.Trim().ToUpperInvariant();
        PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "card" : paymentMethod.Trim().ToLowerInvariant();
        var trimmedLast4 = string.IsNullOrWhiteSpace(cardLast4) ? "4242" : cardLast4.Trim();
        CardLast4 = trimmedLast4.Length == 4 ? trimmedLast4 : "4242";
        Status = status;
        ProviderReference = providerReference.Trim();
        FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
        CreatedAt = nowUtc;
    }
}
