using Davids.Services.Payments.Application.Services;
using Davids.Services.Payments.Core.Repositories;
using Davids.Services.Payments.Infrastructure.Auth;
using Davids.Services.Payments.Infrastructure.Messaging;
using Davids.Services.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Davids.Services.Payments.Infrastructure;

public static class PaymentsInfrastructureExtensions
{
    public const string CustomerScheme = "CustomerBearer";
    public const string AdminScheme = "AdminBearer";

    public static IServiceCollection AddPaymentsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var customerOidc = configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-services" };
        customerOidc.Audience ??= "davids-services";
        customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");
        services.AddSingleton(customerOidc);

        var adminOidc = configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-admin" };
        adminOidc.Audience ??= "davids-admin";
        adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

        var connectionString = configuration.GetConnectionString("paymentsdb")
            ?? throw new InvalidOperationException("Missing paymentsdb connection.");
        services.AddDbContext<PaymentsDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IPaymentsService, PaymentsService>();

        if (configuration.GetValue("Messaging:Enabled", true))
        {
            services.AddHostedService<PaymentsMessageWorker>();
        }

        services.AddHealthChecks().AddCheck<PaymentsDatabaseHealthCheck>("payments-database");
        return services;
    }
}
