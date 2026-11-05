using Davids.Services.Payments.Core.Entities;

namespace Davids.Services.Payments.Core.Repositories;

public interface IPaymentRepository
{
    Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentTransaction>> GetByCustomerAsync(Guid customerId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentTransaction>> SearchAsync(PaymentStatus? status, Guid? customerId, int limit, CancellationToken ct = default);
    Task AddAsync(PaymentTransaction payment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
