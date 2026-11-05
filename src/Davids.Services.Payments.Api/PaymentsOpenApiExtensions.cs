using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Davids.Services.Payments.Api;

public static class PaymentsOpenApiExtensions
{
    public static IServiceCollection AddPaymentsOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Davids Payments API";
                document.Info.Version = "1.0.0";
                document.Info.Description = "Asynchronous RabbitMQ payment capture ledger for customer orders and workforce audit.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["BearerAuth"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "RS256 OIDC access token (CustomerBearer or AdminBearer)."
                };
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
