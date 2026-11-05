using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Davids.Services.Payments.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlServer("Server=localhost;Database=paymentsdb;TrustServerCertificate=True")
            .Options;
        return new PaymentsDbContext(options);
    }
}
