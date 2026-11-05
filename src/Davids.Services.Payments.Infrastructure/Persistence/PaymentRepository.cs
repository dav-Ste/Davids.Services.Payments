using Davids.Services.Payments.Core.Entities;
using Davids.Services.Payments.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Payments.Infrastructure.Persistence;

public sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<PaymentTransaction?> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default)
        => db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.OrderId == orderId, ct);

    public async Task<IReadOnlyList<PaymentTransaction>> GetByCustomerAsync(Guid customerId, int limit, CancellationToken ct = default)
        => await db.Payments
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PaymentTransaction>> SearchAsync(
        PaymentStatus? status,
        Guid? customerId,
        int limit,
        CancellationToken ct = default)
    {
        var query = db.Payments.AsNoTracking().AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (customerId.HasValue)
        {
            query = query.Where(x => x.CustomerId == customerId.Value);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task AddAsync(PaymentTransaction payment, CancellationToken ct = default)
        => db.Payments.AddAsync(payment, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
