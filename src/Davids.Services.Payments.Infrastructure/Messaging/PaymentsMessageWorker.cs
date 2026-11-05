using System.Text;
using System.Text.Json;
using Davids.Services.Payments.Application.Events;
using Davids.Services.Payments.Application.Services;
using Davids.Services.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Davids.Services.Payments.Infrastructure.Messaging;

public sealed class PaymentsMessageWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<PaymentsMessageWorker> logger) : BackgroundService
{
    private const string OrderPlacedQueue = "payments-service/orders.order_placed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Payments RabbitMQ worker disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Missing RabbitMQ messaging connection string.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var publisher = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await using var consumerChannel = await connection.CreateChannelAsync(cancellationToken: ct);

        await publisher.ExchangeDeclareAsync("payments", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.ExchangeDeclareAsync("payments.dead", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueDeclareAsync("payments-service.dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await consumerChannel.QueueBindAsync("payments-service.dead", "payments.dead", "#", cancellationToken: ct);

        var dlxArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "payments.dead" };
        await consumerChannel.QueueDeclareAsync(OrderPlacedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await consumerChannel.QueueBindAsync(OrderPlacedQueue, "orders", "order_placed", cancellationToken: ct);
        await consumerChannel.BasicQosAsync(0, 1, global: false, ct);

        var consumer = new AsyncEventingBasicConsumer(consumerChannel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            var messageId = args.BasicProperties.MessageId;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                messageId = $"order_placed:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(args.Body.Span))[..24]}";
            }

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
                if (!await db.Inbox.AnyAsync(x => x.Id == messageId, ct))
                {
                    var evt = JsonSerializer.Deserialize<OrderPlaced>(args.Body.Span, JsonOptions);
                    if (evt is not null && evt.OrderId != Guid.Empty && evt.CustomerId != Guid.Empty)
                    {
                        var svc = scope.ServiceProvider.GetRequiredService<IPaymentsService>();
                        await svc.ProcessOrderPlacedAsync(evt, ct);
                    }

                    db.Inbox.Add(new InboxMessage { Id = messageId, ReceivedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                }

                await consumerChannel.BasicAckAsync(args.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing order_placed message {MessageId} in Payments.", messageId);
                await consumerChannel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, ct);
            }
        };

        await consumerChannel.BasicConsumeAsync(OrderPlacedQueue, autoAck: false, consumer, ct);

        while (!ct.IsCancellationRequested)
        {
            await PublishPendingOutboxAsync(publisher, ct);
            await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
        }
    }

    private async Task PublishPendingOutboxAsync(IChannel publisher, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var pending = await db.Outbox
            .Where(x => x.PublishedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var msg in pending)
        {
            var props = new BasicProperties
            {
                MessageId = msg.Id,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Type = msg.Type
            };
            await publisher.BasicPublishAsync(
                exchange: "payments",
                routingKey: msg.Type,
                mandatory: false,
                basicProperties: props,
                body: Encoding.UTF8.GetBytes(msg.Payload),
                cancellationToken: ct);
            msg.PublishedAt = DateTime.UtcNow;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
