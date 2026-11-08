using Microsoft.AspNetCore.Authorization;
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

                foreach (var (pathKey, pathItem) in document.Paths ?? [])
                {
                    foreach (var (_, operation) in pathItem.Operations ?? [])
                    {
                        foreach (var parameter in operation.Parameters ?? [])
                        {
                            if (string.Equals(parameter.Name, "limit", StringComparison.Ordinal) &&
                                parameter.Schema is OpenApiSchema limitSchema)
                            {
                                limitSchema.Type = JsonSchemaType.Integer;
                                limitSchema.Minimum = "1";
                                limitSchema.Maximum = string.Equals(pathKey, "/payments/me", StringComparison.Ordinal) ? "200" : "500";
                            }
                            else if (string.Equals(parameter.Name, "status", StringComparison.Ordinal) &&
                                     parameter.Schema is OpenApiSchema statusSchema)
                            {
                                statusSchema.MaxLength = 40;
                                statusSchema.Pattern = @"^[^\u0000]*$";
                            }
                        }

                        if (operation.Security is { Count: > 0 })
                        {
                            operation.Security =
                            [
                                new OpenApiSecurityRequirement
                                {
                                    [new OpenApiSecuritySchemeReference("BearerAuth", document)] = []
                                }
                            ];
                        }
                    }
                }

                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();

                operation.Responses ??= new OpenApiResponses();
                operation.Responses.TryAdd("429", new OpenApiResponse { Description = "Rate limit exceeded." });

                if (requiresAuth)
                {
                    operation.Security = [new OpenApiSecurityRequirement()];
                    operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Missing or invalid RS256 Bearer access token." });
                    operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Caller lacks permission for this payment resource." });
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
