using System.Text.Json;
using Davids.Services.Payments.Application.Services;

namespace Davids.Services.Payments.Infrastructure.Persistence;

public sealed class OutboxWriter(PaymentsDbContext db) : IOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Enqueue<T>(string type, T message)
    {
        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Payload = JsonSerializer.Serialize(message, JsonOptions),
            CreatedAt = DateTime.UtcNow
        });
    }
}
