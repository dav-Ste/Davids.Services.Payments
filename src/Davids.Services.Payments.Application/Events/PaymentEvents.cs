namespace Davids.Services.Payments.Application.Events;

public sealed record OrderPlaced(
    Guid OrderId,
    Guid CustomerId,
    string? CustomerEmail,
    decimal TotalAmount,
    string Currency,
    string PaymentMethod,
    string CardLast4);

public sealed record PaymentCaptured(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string ProviderReference);

public sealed record PaymentFailed(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    string Reason);
