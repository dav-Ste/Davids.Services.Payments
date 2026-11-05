namespace Davids.Services.Payments.Application.DTO;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string Currency,
    string PaymentMethod,
    string CardLast4,
    string Status,
    string ProviderReference,
    string? FailureReason,
    DateTime CreatedAt);
