using Davids.Services.Payments.Application.Services;
using Davids.Services.Payments.Core.Entities;
using Davids.Services.Payments.Infrastructure;
using Davids.Services.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davids.Services.Payments.Tests;

[Trait("Category", "Unit")]
public sealed class ArchitectureTests
{
    [Fact]
    public void CoreLayerHasZeroOuterLayerOrInfrastructureFrameworkDependencies()
    {
        var coreRefs = typeof(PaymentTransaction).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Davids.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("RabbitMQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationLayerDependsOnlyOnCoreAndNeverOnInfrastructureOrApi()
    {
        var appRefs = typeof(PaymentsService).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Davids.Services.Payments.Core", appRefs, StringComparer.Ordinal);
        Assert.DoesNotContain("Davids.Services.Payments.Infrastructure", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.Services.Payments.Api", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.ServiceDefaults", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfrastructureAndApiLayersFollowInwardDependenciesAndNeverCoupleToSiblingServices()
    {
        var infraRefs = typeof(PaymentsDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Davids.Services.Payments.Api", infraRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Products", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Baskets", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Orders", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Customers", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EfCoreRuntimeModelMatchesMigrationsSnapshotWithoutPendingChanges()
    {
        using var designTimeDb = new DesignTimeFactory().CreateDbContext([]);
        Assert.False(designTimeDb.Database.HasPendingModelChanges());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:paymentsdb"] = "Server=localhost;Database=davids_payments;Integrated Security=true;TrustServerCertificate=true",
                ["CustomerOidc:Authority"] = "https://localhost:7504",
                ["AdminOidc:Authority"] = "https://localhost:17325",
                ["Messaging:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPaymentsInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtimeDb = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.False(runtimeDb.Database.HasPendingModelChanges());
    }
}
