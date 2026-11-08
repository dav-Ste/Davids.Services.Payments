using System.Security.Claims;
using Davids.ServiceDefaults;
using Davids.Services.Payments.Api;
using Davids.Services.Payments.Api.Exceptions;
using Davids.Services.Payments.Application.DTO;
using Davids.Services.Payments.Application.Services;
using Davids.Services.Payments.Infrastructure;
using Davids.Services.Payments.Infrastructure.Auth;
using Davids.Services.Payments.Infrastructure.Persistence;
using Davids.Services.Payments.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.LoadVaultAsync();
builder.AddServiceDefaults("davids-payments");
builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);
builder.Services.AddPaymentsInfrastructure(builder.Configuration);

var customerOidc = builder.Configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-services" };
customerOidc.Audience ??= "davids-services";
var customerStaticKey = customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");

var adminOidc = builder.Configuration.GetSection("AdminOidc").Get<OidcAuthoritySettings>()
    ?? new OidcAuthoritySettings { Audience = "davids-admin" };
adminOidc.Audience ??= "davids-admin";
var adminStaticKey = adminOidc.ValidateAndResolveStaticPublicKey("AdminOidc");

var requireHttpsMetadata =
    builder.Environment.IsProduction() ||
    !(builder.Environment.IsDevelopment() ||
      builder.Environment.IsEnvironment("Testing") ||
      builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment"));

builder.Services.AddAuthentication(PaymentsInfrastructureExtensions.CustomerScheme)
    .AddJwtBearer(PaymentsInfrastructureExtensions.CustomerScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(customerOidc.MetadataAddress))
        {
            options.MetadataAddress = customerOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(customerOidc.EffectiveIssuer),
            ValidIssuer = customerOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = customerOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = customerStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!string.Equals(role, DavidsRoles.Customer, StringComparison.Ordinal))
                {
                    context.Fail("CustomerBearer accepts customer ('user') tokens only.");
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer(PaymentsInfrastructureExtensions.AdminScheme, options =>
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        if (!string.IsNullOrWhiteSpace(adminOidc.MetadataAddress))
        {
            options.MetadataAddress = adminOidc.MetadataAddress;
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(adminOidc.EffectiveIssuer),
            ValidIssuer = adminOidc.EffectiveIssuer,
            ValidateAudience = true,
            ValidAudience = adminOidc.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = adminStaticKey,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var role = context.Principal?.FindFirstValue("role");
                if (!DavidsRoles.CanReadWorkforce(role))
                {
                    context.Fail("AdminBearer accepts workforce roles (admin, support, auditor) only.");
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("CustomerOnly", policy =>
    {
        policy.AddAuthenticationSchemes(PaymentsInfrastructureExtensions.CustomerScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Customer);
    })
    .AddPolicy("WorkforceRead", policy =>
    {
        policy.AddAuthenticationSchemes(PaymentsInfrastructureExtensions.AdminScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("role", DavidsRoles.Admin, DavidsRoles.Support, DavidsRoles.Auditor);
    });

builder.Services.AddExceptionHandler<PaymentApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddPaymentsOpenApi();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Automatic migration is development-only.");
    }

    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
}

app.UseTrustedForwardedHeaders();
app.UseExceptionHandler();
if (!(app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Http:AllowInsecureDevelopment")))
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "docs";
        options.SwaggerEndpoint("../openapi/v1.json", "Davids Payments v1");
    });
}

static bool TryGetCustomerId(ClaimsPrincipal user, out Guid customerId)
{
    var raw = user.FindFirstValue("sub");
    return Guid.TryParse(raw, out customerId) && customerId != Guid.Empty;
}

app.MapGet("/", () => Results.Text("Davids Payments Service"))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.MapGet("/payments/me", async (int? limit, ClaimsPrincipal user, IPaymentsService service, CancellationToken ct) =>
    {
        if (limit is <= 0 or > 200)
        {
            return Results.BadRequest(new { code = "invalid_query", reason = "Limit must be between 1 and 200." });
        }

        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await service.GetCustomerPaymentsAsync(customerId, limit ?? 50, ct));
    })
    .Produces<IReadOnlyList<PaymentDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/payments/me/order/{orderId:guid}", async (Guid orderId, ClaimsPrincipal user, IPaymentsService service, CancellationToken ct) =>
    {
        if (!TryGetCustomerId(user, out var customerId))
        {
            return Results.Unauthorized();
        }

        var payment = await service.GetCustomerPaymentByOrderAsync(customerId, orderId, ct);
        return payment is null ? Results.NotFound() : Results.Ok(payment);
    })
    .Produces<PaymentDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .RequireAuthorization("CustomerOnly");

app.MapGet("/payments", async (string? status, Guid? customerId, int? limit, IPaymentsService service, CancellationToken ct) =>
    {
        if ((status is not null && (status.Length > 40 || status.Contains('\0'))) ||
            limit is <= 0 or > 500)
        {
            return Results.BadRequest(new { code = "invalid_query", reason = "Query parameters must be within bounds without control bytes." });
        }

        return Results.Ok(await service.SearchWorkforceAsync(status, customerId, limit ?? 100, ct));
    })
    .Produces<IReadOnlyList<PaymentDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .RequireAuthorization("WorkforceRead");

app.MapGet("/payments/order/{orderId:guid}", async (Guid orderId, IPaymentsService service, CancellationToken ct) =>
    {
        var payment = await service.GetWorkforcePaymentByOrderAsync(orderId, ct);
        return payment is null ? Results.NotFound() : Results.Ok(payment);
    })
    .Produces<PaymentDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound)
    .RequireAuthorization("WorkforceRead");

app.Run();

public partial class Program;
