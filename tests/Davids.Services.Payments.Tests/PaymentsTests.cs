using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Davids.Services.Payments.Application.DTO;
using Davids.Services.Payments.Application.Events;
using Davids.Services.Payments.Application.Services;
using Davids.Services.Payments.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Davids.Services.Payments.Tests;

public sealed class PaymentsFactory : WebApplicationFactory<Program>
{
    private const string CustomerIssuer = "https://identity.davids.test/";
    private const string AdminIssuer = "https://admin-identity.davids.test/";

    private static readonly RSA CustomerRsa = RSA.Create(2048);
    private static readonly RSA AdminRsa = RSA.Create(2048);

    private static readonly string CustomerPublicKeyPem = CustomerRsa.ExportSubjectPublicKeyInfoPem();
    private static readonly string AdminPublicKeyPem = AdminRsa.ExportSubjectPublicKeyInfoPem();

    private static readonly RsaSecurityKey CustomerKey = CreateKey(CustomerRsa);
    private static readonly RsaSecurityKey AdminKey = CreateKey(AdminRsa);

    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public PaymentsFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CustomerOidc:Issuer", CustomerIssuer);
        builder.UseSetting("CustomerOidc:Audience", "davids-services");
        builder.UseSetting("CustomerOidc:PublicKeyPem", CustomerPublicKeyPem);
        builder.UseSetting("AdminOidc:Issuer", AdminIssuer);
        builder.UseSetting("AdminOidc:Audience", "davids-admin");
        builder.UseSetting("AdminOidc:PublicKeyPem", AdminPublicKeyPem);
        builder.UseSetting("Vault:Enabled", "false");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("ConnectionStrings:paymentsdb", "unused");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PaymentsDbContext>>();
            services.RemoveAll<PaymentsDbContext>();
            services.AddDbContext<PaymentsDbContext>(options => options.UseSqlite(connection));
        });
    }

    public async Task<HttpClient> CreatePaymentsClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.EnsureCreatedAsync();
        return client;
    }

    public static string CreateCustomerToken(Guid sub) => Mint(sub, "user", CustomerIssuer, "davids-services", CustomerKey);
    public static string CreateWorkforceToken(Guid sub, string role = "support") => Mint(sub, role, AdminIssuer, "davids-admin", AdminKey);

    private static string Mint(Guid sub, string role, string issuer, string audience, RsaSecurityKey key)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", sub.ToString("N")), new Claim("role", role), new Claim("jti", Guid.NewGuid().ToString("N"))],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static RsaSecurityKey CreateKey(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(spki))[..16];
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = kid };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}

[Trait("Category", "Integration")]
public sealed class PaymentsTests
{
    [Fact]
    public async Task ProcessOrderPlacedCapturesNormalCardsDeclines0002IdempotentlyAndEnforcesRealmIsolation()
    {
        await using var factory = new PaymentsFactory();
        using var client = await factory.CreatePaymentsClientAsync();

        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var orderCaptured = Guid.NewGuid();
        var orderDeclined = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPaymentsService>();
            var cap1 = await svc.ProcessOrderPlacedAsync(new OrderPlaced(orderCaptured, customerA, "alice@davids.test", 199.00m, "GBP", "card", "4242"));
            var cap2 = await svc.ProcessOrderPlacedAsync(new OrderPlaced(orderCaptured, customerA, "alice@davids.test", 199.00m, "GBP", "card", "4242"));
            Assert.Equal(cap1.Id, cap2.Id);
            Assert.Equal("captured", cap1.Status);

            var dec = await svc.ProcessOrderPlacedAsync(new OrderPlaced(orderDeclined, customerA, "alice@davids.test", 89.00m, "GBP", "card", "0002"));
            Assert.Equal("declined", dec.Status);

            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            Assert.Single(await db.Outbox.Where(m => m.Type == "payment_captured").ToListAsync());
            Assert.Single(await db.Outbox.Where(m => m.Type == "payment_failed").ToListAsync());
        }

        // Customer A can inspect own payment
        using var meReq = new HttpRequestMessage(HttpMethod.Get, $"/payments/me/order/{orderCaptured}");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PaymentsFactory.CreateCustomerToken(customerA));
        var meResp = await client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

        // Customer B cannot read Customer A's payment
        using var bolaReq = new HttpRequestMessage(HttpMethod.Get, $"/payments/me/order/{orderCaptured}");
        bolaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PaymentsFactory.CreateCustomerToken(customerB));
        var bolaResp = await client.SendAsync(bolaReq);
        Assert.Equal(HttpStatusCode.NotFound, bolaResp.StatusCode);

        // Customer cannot access workforce /payments ledger
        using var custAdminReq = new HttpRequestMessage(HttpMethod.Get, "/payments");
        custAdminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PaymentsFactory.CreateCustomerToken(customerA));
        var custAdminResp = await client.SendAsync(custAdminReq);
        Assert.Equal(HttpStatusCode.Unauthorized, custAdminResp.StatusCode);

        // Workforce support can inspect ledger
        using var wfReq = new HttpRequestMessage(HttpMethod.Get, "/payments");
        wfReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", PaymentsFactory.CreateWorkforceToken(Guid.NewGuid(), "support"));
        var wfResp = await client.SendAsync(wfReq);
        Assert.Equal(HttpStatusCode.OK, wfResp.StatusCode);
        var list = await wfResp.Content.ReadFromJsonAsync<List<PaymentDto>>();
        Assert.Equal(2, list?.Count);
    }
}
