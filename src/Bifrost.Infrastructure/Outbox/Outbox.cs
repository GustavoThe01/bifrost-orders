using System.Text.Json;
using Bifrost.Domain.Common;
using Bifrost.Domain.Orders;
using Bifrost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Bifrost.Infrastructure.Outbox;

/// <summary>
/// Um evento de domínio esperando para ser publicado. É gravado na mesma transação da mudança que o gerou:
/// ou os dois ficam, ou nenhum fica.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Nome do evento, como <c>OrderConfirmed</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Dados do evento em JSON, com os ids como texto.</summary>
    public required string Payload { get; init; }

    /// <summary>UTC. <see cref="DateTime"/>, e não DateTimeOffset, porque o SQLite do EF Core só ordena DateTime.</summary>
    public DateTime OccurredAt { get; init; }

    public DateTime? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Converte cada evento de domínio num payload estável, sem os tipos internos do domínio.</summary>
public static class OutboxPayload
{
    public static (string Type, string Payload) From(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        object data = domainEvent switch
        {
            OrderCreated e => new { orderId = e.OrderId.Value, customerId = e.CustomerId.Value },
            OrderConfirmed e => new { orderId = e.OrderId.Value, total = e.Total.Amount, currency = e.Total.Currency },
            OrderPaid e => new { orderId = e.OrderId.Value },
            OrderShipped e => new { orderId = e.OrderId.Value },
            OrderDelivered e => new { orderId = e.OrderId.Value },
            OrderCancelled e => new { orderId = e.OrderId.Value, previousStatus = e.PreviousStatus.ToString(), reason = e.Reason },
            _ => domainEvent,
        };
        return (domainEvent.GetType().Name, JsonSerializer.Serialize(data, data.GetType()));
    }
}

/// <summary>
/// Antes de cada <c>SaveChanges</c>, recolhe os eventos dos agregados alterados e os acrescenta como mensagens do
/// Outbox. Como entram no mesmo <c>SaveChanges</c>, entram na mesma transação do pedido.
/// </summary>
public sealed class OutboxInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            AddMessages(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            AddMessages(context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void AddMessages(DbContext context)
    {
        var aggregates = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();
        if (aggregates.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var (type, payload) = OutboxPayload.From(domainEvent);
                context.Set<OutboxMessage>().Add(new OutboxMessage { Id = Guid.CreateVersion7(), Type = type, Payload = payload, OccurredAt = now });
            }

            // Limpa logo: se o mesmo agregado for salvo de novo, o evento não é gravado duas vezes.
            aggregate.ClearDomainEvents();
        }
    }
}

/// <summary>Para onde as mensagens vão. Hoje, o log; no futuro, um broker (RabbitMQ, Service Bus).</summary>
public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed partial class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogPublished(logger, message.Type, message.Id, message.Payload);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Evento {EventType} publicado ({MessageId}): {Payload}")]
    private static partial void LogPublished(ILogger logger, string eventType, Guid messageId, string payload);
}

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Liga o processamento em segundo plano. Os testes que olham as mensagens pendentes desligam.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 20;

    /// <summary>Depois dessas tentativas, a mensagem para de ser processada e fica para análise (LastError).</summary>
    public int MaxAttempts { get; set; } = 5;
}

/// <summary>
/// Publica um lote de mensagens pendentes, na ordem em que aconteceram, e marca cada uma como processada.
/// </summary>
/// <remarks>
/// A entrega é "pelo menos uma vez": se a publicação der certo e a gravação do <c>ProcessedAt</c> falhar, a mensagem
/// será publicada de novo. Quem consome precisa tolerar repetição (pelo id da mensagem). "Exatamente uma vez" exigiria
/// uma transação entre o banco e o broker, que não existe.
/// </remarks>
public sealed partial class OutboxDispatcher(BifrostDbContext context, IOutboxPublisher publisher, TimeProvider clock, ILogger<OutboxDispatcher> logger)
{
    /// <returns>Quantas mensagens foram publicadas com sucesso.</returns>
    public async Task<int> ProcessBatchAsync(OutboxOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var pending = await context.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null && m.Attempts < options.MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.Id)
            .Take(options.BatchSize)
            .ToListAsync(cancellationToken);

        var published = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, cancellationToken);
                message.ProcessedAt = clock.GetUtcNow().UtcDateTime;
                message.LastError = null;
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
                LogFailed(logger, message.Type, message.Id, message.Attempts, exception);
            }

            // Grava mensagem a mensagem: uma falha no meio do lote não desfaz as que já foram publicadas.
            await context.SaveChangesAsync(cancellationToken);
        }

        return published;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao publicar {EventType} ({MessageId}), tentativa {Attempt}")]
    private static partial void LogFailed(ILogger logger, string eventType, Guid messageId, int attempt, Exception exception);
}
