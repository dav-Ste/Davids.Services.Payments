using Davids.Services.Payments.Application.DTO;
using Davids.Services.Payments.Application.Events;
using Davids.Services.Payments.Core.Entities;
using Davids.Services.Payments.Core.Repositories;

namespace Davids.Services.Payments.Application.Services;

public interface IOutboxWriter
{
    void Enqueue<T>(string type, T message);
}

public interface IPaymentsService
{
    Task<PaymentDto> ProcessOrderPlacedAsync(OrderPlaced evt, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentDto>> GetCustomerPaymentsAsync(Guid customerId, int limit = 50, CancellationToken ct = default);
    Task<PaymentDto?> GetCustomerPaymentByOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default);
    Task<PaymentDto?> GetWorkforcePaymentByOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentDto>> SearchWorkforceAsync(string? status, Guid? customerId, int limit = 100, CancellationToken ct = default);
}

public sealed class PaymentsService(IPaymentRepository repository, IOutboxWriter outbox) : IPaymentsService
{
    private const string SimulatedDeclineLast4 = "0002";

    public async Task<PaymentDto> ProcessOrderPlacedAsync(OrderPlaced evt, CancellationToken ct = default)
    {
        var existing = await repository.GetByOrderIdAsync(evt.OrderId, ct);
        if (existing is not null)
        {
            return Map(existing);
        }

        var paymentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var providerRef = $"PAY-{paymentId.ToString("N")[..12].ToUpperInvariant()}";

        PaymentTransaction tx;
        if (string.Equals(evt.CardLast4?.Trim(), SimulatedDeclineLast4, StringComparison.Ordinal))
        {
            const string reason = "Payment authorization declined by card issuer (card ending 0002).";
            tx = new PaymentTransaction(
                paymentId,
                evt.OrderId,
                evt.CustomerId,
                evt.TotalAmount,
                evt.Currency,
                evt.PaymentMethod,
                evt.CardLast4,
                PaymentStatus.Declined,
                providerRef,
                reason,
                now);
            await repository.AddAsync(tx, ct);
            outbox.Enqueue("payment_failed", new PaymentFailed(tx.Id, tx.OrderId, tx.CustomerId, reason));
        }
        else
        {
            tx = new PaymentTransaction(
                paymentId,
                evt.OrderId,
                evt.CustomerId,
                evt.TotalAmount,
                evt.Currency,
                evt.PaymentMethod,
                evt.CardLast4,
                PaymentStatus.Captured,
                providerRef,
                failureReason: null,
                now);
            await repository.AddAsync(tx, ct);
            outbox.Enqueue("payment_captured", new PaymentCaptured(
                tx.Id,
                tx.OrderId,
                tx.CustomerId,
                tx.Amount,
                tx.Currency,
                tx.ProviderReference));
        }

        await repository.SaveChangesAsync(ct);
        return Map(tx);
    }

    public async Task<IReadOnlyList<PaymentDto>> GetCustomerPaymentsAsync(Guid customerId, int limit = 50, CancellationToken ct = default)
    {
        var items = await repository.GetByCustomerAsync(customerId, Math.Clamp(limit, 1, 200), ct);
        return items.Select(Map).ToArray();
    }

    public async Task<PaymentDto?> GetCustomerPaymentByOrderAsync(Guid customerId, Guid orderId, CancellationToken ct = default)
    {
        var tx = await repository.GetByOrderIdAsync(orderId, ct);
        return tx is not null && tx.CustomerId == customerId ? Map(tx) : null;
    }

    public async Task<PaymentDto?> GetWorkforcePaymentByOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var tx = await repository.GetByOrderIdAsync(orderId, ct);
        return tx is null ? null : Map(tx);
    }

    public async Task<IReadOnlyList<PaymentDto>> SearchWorkforceAsync(
        string? status,
        Guid? customerId,
        int limit = 100,
        CancellationToken ct = default)
    {
        PaymentStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status.Trim(), ignoreCase: true, out var st))
        {
            parsed = st;
        }

        var items = await repository.SearchAsync(parsed, customerId, Math.Clamp(limit, 1, 500), ct);
        return items.Select(Map).ToArray();
    }

    private static PaymentDto Map(PaymentTransaction tx) => new(
        tx.Id,
        tx.OrderId,
        tx.CustomerId,
        tx.Amount,
        tx.Currency,
        tx.PaymentMethod,
        tx.CardLast4,
        tx.Status == PaymentStatus.Captured ? "captured" : "declined",
        tx.ProviderReference,
        tx.FailureReason,
        tx.CreatedAt);
}
