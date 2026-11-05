using Davids.Services.Payments.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Payments.Infrastructure.Persistence;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<PaymentTransaction> Payments => Set<PaymentTransaction>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentTransaction>(entity =>
        {
            entity.ToTable("PaymentTransactions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            entity.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PaymentMethod).HasMaxLength(24).IsRequired();
            entity.Property(x => x.CardLast4).HasMaxLength(4).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(x => x.ProviderReference).HasMaxLength(64).IsRequired();
            entity.Property(x => x.FailureReason).HasMaxLength(400);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("Outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(64);
            entity.Property(x => x.Type).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.PublishedAt);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("Inbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
        });
    }
}

public sealed class OutboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class InboxMessage
{
    public string Id { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
